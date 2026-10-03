using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DragoAnt.System.Text.Json.Observer.Http;

public class JsonBodyLoggingHandler : DelegatingHandler
{
    private readonly string _clientName;
    private readonly IOptionsMonitor<JsonBodyLoggingOptions>? _optionsMonitor;
    private readonly JsonBodyLoggingOptions? _staticOptions;
    private readonly IJsonBodyMaskerProvider? _maskerProvider;
    private readonly IJsonBodyLogSink _sink;
    private readonly ILogger _logger;

    public JsonBodyLoggingHandler(
        string clientName,
        IOptionsMonitor<JsonBodyLoggingOptions> optionsMonitor,
        IJsonBodyMaskerProvider? maskerProvider = null,
        IJsonBodyLogSink? sink = null,
        ILogger<JsonBodyLoggingHandler>? logger = null)
    {
        _clientName = clientName ?? string.Empty;
        _optionsMonitor = optionsMonitor;
        _maskerProvider = maskerProvider;
        _logger = logger ?? NullLogger<JsonBodyLoggingHandler>.Instance;
        _sink = sink ?? new LoggerJsonBodyLogSink(_logger);
    }

    public JsonBodyLoggingHandler(
        JsonBodyLoggingOptions options,
        IJsonBodyMaskerProvider? maskerProvider = null,
        IJsonBodyLogSink? sink = null,
        ILogger<JsonBodyLoggingHandler>? logger = null)
    {
        _clientName = string.Empty;
        _staticOptions = options ?? new JsonBodyLoggingOptions();
        _maskerProvider = maskerProvider;
        _logger = logger ?? NullLogger<JsonBodyLoggingHandler>.Instance;
        _sink = sink ?? new LoggerJsonBodyLogSink(_logger);
    }

    private JsonBodyLoggingOptions Options =>
        _optionsMonitor != null
            ? _optionsMonitor.Get(_clientName)
            : (_staticOptions ?? new JsonBodyLoggingOptions());

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startTimestamp = Stopwatch.GetTimestamp();
        HttpResponseMessage? response = null;
        Exception? caughtException = null;
        var outcome = JsonBodyOutcome.Success;

        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                outcome = JsonBodyOutcome.Failure;
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            outcome = JsonBodyOutcome.Timeout;
            caughtException = ex;
        }
        catch (Exception ex)
        {
            outcome = JsonBodyOutcome.Exception;
            caughtException = ex;
        }

        // Caller cancellation is never logged
        if (cancellationToken.IsCancellationRequested)
        {
            if (caughtException != null)
            {
                throw caughtException;
            }
            return response!;
        }

        var options = Options;
        bool shouldLog = options.When switch
        {
            JsonBodyLogWhen.Always => true,
            JsonBodyLogWhen.OnFailure => outcome != JsonBodyOutcome.Success,
            JsonBodyLogWhen.Never => false,
            _ => false
        };

        if (shouldLog)
        {
            try
            {
                await LogBodyAsync(request, response, outcome, caughtException, startTimestamp, options).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log HTTP body for client {ClientName}", _clientName);
            }
        }

        if (caughtException != null)
        {
            throw caughtException;
        }

        return response!;
    }

    private async Task LogBodyAsync(
        HttpRequestMessage request,
        HttpResponseMessage? response,
        JsonBodyOutcome outcome,
        Exception? caughtException,
        long startTimestamp,
        JsonBodyLoggingOptions options)
    {
        request.Options.TryGetValue(JsonBodyLogging.Key, out var context);

        var (requestBody, reqUnmasked, reqTruncated) = await ProcessRequestBodyAsync(request, context, options).ConfigureAwait(false);
        var (responseBody, respUnmasked, respTruncated) = await ProcessResponseBodyAsync(response, context, options).ConfigureAwait(false);

        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        var path = request.RequestUri?.PathAndQuery ?? request.RequestUri?.ToString() ?? string.Empty;
        var statusCode = response != null ? (int)response.StatusCode : (int?)null;

        var entry = new JsonBodyLogEntry(
            ClientName: _clientName,
            Method: request.Method,
            Path: path,
            StatusCode: statusCode,
            ElapsedMs: elapsedMs,
            Outcome: outcome,
            RequestBody: requestBody,
            ResponseBody: responseBody,
            BodyUnmasked: reqUnmasked || respUnmasked,
            Truncated: reqTruncated || respTruncated,
            Exception: caughtException);

        _sink.Write(entry);
    }

    private async Task<(string? Body, bool Unmasked, bool Truncated)> ProcessRequestBodyAsync(
        HttpRequestMessage request,
        JsonBodyLoggingContext? context,
        JsonBodyLoggingOptions options)
    {
        if (request.Content == null)
        {
            return (null, false, false);
        }

        var mediaType = request.Content.Headers.ContentType?.MediaType;
        if (!IsJsonMediaType(mediaType))
        {
            return ($"[body not logged: {mediaType ?? "unknown"}]", false, false);
        }

        byte[]? bytes = null;
        bool isUnseekableStream = false;

        if (request.Content is StringContent or ByteArrayContent or ReadOnlyMemoryContent)
        {
            bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        else if (request.Content is StreamContent)
        {
            var stream = await request.Content.ReadAsStreamAsync().ConfigureAwait(false);
            if (stream.CanSeek)
            {
                var origPos = stream.Position;
                stream.Position = 0;
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms).ConfigureAwait(false);
                stream.Position = origPos;
                bytes = ms.ToArray();
            }
            else
            {
                isUnseekableStream = true;
            }
        }
        else
        {
            // JsonContent or other HttpContent
            try
            {
                bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
            catch
            {
                isUnseekableStream = true;
            }
        }

        if (isUnseekableStream || bytes == null)
        {
            return ("[body not buffered]", false, false);
        }

        bool truncated = false;
        if (bytes.Length > options.MaxBodyBytes)
        {
            bytes = bytes.AsSpan(0, options.MaxBodyBytes).ToArray();
            truncated = true;
        }

        var reqType = context?.RequestType ?? options.DefaultRequestType;
        return MaskContent(bytes, reqType, options, truncated);
    }

    private async Task<(string? Body, bool Unmasked, bool Truncated)> ProcessResponseBodyAsync(
        HttpResponseMessage? response,
        JsonBodyLoggingContext? context,
        JsonBodyLoggingOptions options)
    {
        if (response?.Content == null)
        {
            return (null, false, false);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!IsJsonMediaType(mediaType))
        {
            return ($"[body not logged: {mediaType ?? "unknown"}]", false, false);
        }

        var originalStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

        // Read up to MaxBodyBytes + 1 to detect truncation
        int bufferSize = options.MaxBodyBytes + 1;
        var prefixBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        int totalRead = 0;

        try
        {
            while (totalRead < bufferSize)
            {
                int read = await originalStream.ReadAsync(prefixBuffer.AsMemory(totalRead, bufferSize - totalRead)).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                totalRead += read;
            }

            bool truncated = totalRead > options.MaxBodyBytes;
            int bytesToMask = Math.Min(totalRead, options.MaxBodyBytes);
            var capturedBytes = prefixBuffer.AsSpan(0, bytesToMask).ToArray();

            // Replace response.Content with PrefixRemainderStream so the caller reads the complete response body
            var prefixRemainderStream = new PrefixRemainderStream(new ReadOnlyMemory<byte>(prefixBuffer, 0, totalRead), originalStream);
            var newContent = new StreamContent(prefixRemainderStream);

            foreach (var header in response.Content.Headers)
            {
                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = newContent;

            var respType = context?.ResponseType ?? options.DefaultResponseType;
            return MaskContent(capturedBytes, respType, options, truncated);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(prefixBuffer);
        }
    }

    private (string? Body, bool Unmasked, bool Truncated) MaskContent(
        byte[] utf8Bytes,
        Type? modelType,
        JsonBodyLoggingOptions options,
        bool truncated)
    {
        if (options.IncludeSensitive)
        {
            var raw = Encoding.UTF8.GetString(utf8Bytes);
            return (raw, true, truncated);
        }

        if (_maskerProvider != null)
        {
            var masker = _maskerProvider.GetMasker(modelType, _clientName);
            if (masker == null)
            {
                return ("[body withheld]", false, truncated);
            }

            var writer = new ArrayBufferWriter<byte>();
            var result = masker.Mask(utf8Bytes, writer, options: new JsonObserverOptions(MaxOutputBytes: options.MaxBodyBytes));
            var masked = Encoding.UTF8.GetString(writer.WrittenSpan);
            return (masked, false, truncated || result.Status == MaskStatus.Truncated);
        }

        // Without a specific masker, mask with default allow-list
        var defaultMasker = JsonObserver.Obj(rules => { });
        var defaultWriter = new ArrayBufferWriter<byte>();
        var defaultResult = defaultMasker.Mask(utf8Bytes, defaultWriter, options: new JsonObserverOptions(MaxOutputBytes: options.MaxBodyBytes));
        var defaultMasked = Encoding.UTF8.GetString(defaultWriter.WrittenSpan);
        return (defaultMasked, false, truncated || defaultResult.Status == MaskStatus.Truncated);
    }

    private static bool IsJsonMediaType(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
        {
            return true; // Default assume JSON when content is present
        }

        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PrefixRemainderStream : Stream
    {
        private readonly ReadOnlyMemory<byte> _prefix;
        private int _prefixPosition;
        private readonly Stream _remainder;

        public PrefixRemainderStream(ReadOnlyMemory<byte> prefix, Stream remainder)
        {
            _prefix = prefix;
            _remainder = remainder;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            int bytesRead = 0;
            if (_prefixPosition < _prefix.Length)
            {
                int available = _prefix.Length - _prefixPosition;
                int toCopy = Math.Min(available, buffer.Length);
                _prefix.Span.Slice(_prefixPosition, toCopy).CopyTo(buffer);
                _prefixPosition += toCopy;
                bytesRead += toCopy;

                if (bytesRead == buffer.Length)
                {
                    return bytesRead;
                }
            }

            int remainderRead = _remainder.Read(buffer.Slice(bytesRead));
            return bytesRead + remainderRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int bytesRead = 0;
            if (_prefixPosition < _prefix.Length)
            {
                int available = _prefix.Length - _prefixPosition;
                int toCopy = Math.Min(available, buffer.Length);
                _prefix.Slice(_prefixPosition, toCopy).CopyTo(buffer);
                _prefixPosition += toCopy;
                bytesRead += toCopy;

                if (bytesRead == buffer.Length)
                {
                    return bytesRead;
                }
            }

            int remainderRead = await _remainder.ReadAsync(buffer.Slice(bytesRead), cancellationToken).ConfigureAwait(false);
            return bytesRead + remainderRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush() => _remainder.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _remainder.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _remainder.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
