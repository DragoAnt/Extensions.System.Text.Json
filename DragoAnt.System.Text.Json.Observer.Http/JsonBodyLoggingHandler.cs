using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// A <see cref="DelegatingHandler"/> that logs masked JSON request and response bodies of an <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Register it with <see cref="HttpClientBuilderExtensions.AddJsonBodyLogging"/>; construct it directly only for a manually
/// built handler chain. Logging never changes what the caller receives: the response body stays readable in full, streaming
/// responses (<see cref="HttpCompletionOption.ResponseHeadersRead"/>) are returned as soon as their headers arrive, and the
/// original exception of a failed call is rethrown with its stack trace. A failure inside logging is reported through the
/// handler's <see cref="ILogger"/> and never reaches the caller.
/// </para>
/// <para>
/// The response body is read for logging in the background, up to <see cref="JsonBodyLoggingOptions.MaxBodyBytes"/>, and the
/// entry is written when that read ends: when the limit is reached, the body ends, the read fails, or the caller disposes the response.
/// </para>
/// </remarks>
public sealed class JsonBodyLoggingHandler : DelegatingHandler
{
    private static readonly JsonObserver DefaultMasker = JsonObserver.Any(static _ => { }, static _ => { });

    private readonly string _clientName;
    private readonly IOptionsMonitor<JsonBodyLoggingOptions>? _optionsMonitor;
    private readonly JsonBodyLoggingOptions? _fixedOptions;
    private readonly IJsonBodyMaskerProvider? _maskerProvider;
    private readonly IJsonBodyLogSink _sink;
    private readonly ILogger _logger;
    private int _failureReported;
    private int _sensitiveReported;

    /// <summary>
    /// Creates a handler that reads the options named <paramref name="clientName"/> from <paramref name="optionsMonitor"/> on every call,
    /// so option changes apply to the next call.
    /// </summary>
    /// <param name="clientName">Client name; selects the named options and is written to every entry.</param>
    /// <param name="optionsMonitor">Source of the named options.</param>
    /// <param name="maskerProvider">Chooses the masker per body; when <c>null</c>, every value is masked.</param>
    /// <param name="sink">Receives the entries; when <c>null</c>, they are written to <paramref name="logger"/>.</param>
    /// <param name="logger">Logger for the default sink and for failures inside logging.</param>
    public JsonBodyLoggingHandler(
        string clientName,
        IOptionsMonitor<JsonBodyLoggingOptions> optionsMonitor,
        IJsonBodyMaskerProvider? maskerProvider = null,
        IJsonBodyLogSink? sink = null,
        ILogger<JsonBodyLoggingHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        _clientName = clientName ?? string.Empty;
        _optionsMonitor = optionsMonitor;
        _maskerProvider = maskerProvider;
        _logger = logger ?? NullLogger<JsonBodyLoggingHandler>.Instance;
        _sink = sink ?? new LoggerJsonBodyLogSink(_logger);
    }

    /// <summary>
    /// Creates a handler with fixed options and an empty client name.
    /// </summary>
    /// <param name="options">Options used for every call; defaults when <c>null</c>.</param>
    /// <param name="maskerProvider">Chooses the masker per body; when <c>null</c>, every value is masked.</param>
    /// <param name="sink">Receives the entries; when <c>null</c>, they are written to <paramref name="logger"/>.</param>
    /// <param name="logger">Logger for the default sink and for failures inside logging.</param>
    public JsonBodyLoggingHandler(
        JsonBodyLoggingOptions? options,
        IJsonBodyMaskerProvider? maskerProvider = null,
        IJsonBodyLogSink? sink = null,
        ILogger<JsonBodyLoggingHandler>? logger = null)
    {
        _clientName = string.Empty;
        _fixedOptions = options ?? new JsonBodyLoggingOptions();
        _maskerProvider = maskerProvider;
        _logger = logger ?? NullLogger<JsonBodyLoggingHandler>.Instance;
        _sink = sink ?? new LoggerJsonBodyLogSink(_logger);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _optionsMonitor?.Get(_clientName) ?? _fixedOptions!;
        var started = Stopwatch.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var failure = ExceptionDispatchInfo.Capture(ex);
            if (options.When != JsonBodyLogWhen.Never)
            {
                var outcome = ex is OperationCanceledException ? JsonBodyOutcome.Canceled : JsonBodyOutcome.Exception;
                await TryLogAsync(request, response: null, outcome, ex, started, options).ConfigureAwait(false);
            }

            failure.Throw();
            throw;
        }

        var result = response.IsSuccessStatusCode ? JsonBodyOutcome.Success : JsonBodyOutcome.Failure;
        if (options.When == JsonBodyLogWhen.Always || (options.When == JsonBodyLogWhen.OnFailure && result != JsonBodyOutcome.Success))
        {
            await TryLogAsync(request, response, result, exception: null, started, options).ConfigureAwait(false);
        }

        return response;
    }

    private async Task TryLogAsync(
        HttpRequestMessage request,
        HttpResponseMessage? response,
        JsonBodyOutcome outcome,
        Exception? exception,
        long started,
        JsonBodyLoggingOptions options)
    {
        try
        {
            await LogAsync(request, response, outcome, exception, started, options).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    private async Task LogAsync(
        HttpRequestMessage request,
        HttpResponseMessage? response,
        JsonBodyOutcome outcome,
        Exception? exception,
        long started,
        JsonBodyLoggingOptions options)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var limit = Math.Clamp(options.MaxBodyBytes, 0, Array.MaxLength - 1);
        request.Options.TryGetValue(JsonBodyLogging.Key, out var context);

        if (options.IncludeSensitive && Interlocked.Exchange(ref _sensitiveReported, 1) == 0)
        {
            _logger.LogWarning("HTTP body logging for client {ClientName} writes bodies unmasked because IncludeSensitive is on", _clientName);
        }

        var requestBody = await ReadRequestAsync(request.Content, context?.RequestType ?? options.DefaultRequestType, options, limit)
            .ConfigureAwait(false);

        var entry = new JsonBodyLogEntry
        {
            ClientName = _clientName,
            Operation = context?.Operation,
            Method = request.Method,
            Path = PathOf(request.RequestUri),
            StatusCode = response is null ? null : (int)response.StatusCode,
            ElapsedMs = elapsedMs,
            Outcome = outcome,
            RequestBody = requestBody.Text,
            RequestBodyStatus = requestBody.Status,
            BodyUnmasked = requestBody.Status == JsonBodyStatus.Raw,
            Truncated = requestBody.Truncated,
            Exception = exception,
        };

        if (response?.Content is null)
        {
            Write(entry);
            return;
        }

        if (SkipReason(response.Content.Headers, limit) is { } reason)
        {
            Write(entry with { ResponseBody = $"[body not logged: {reason}]", ResponseBodyStatus = JsonBodyStatus.Skipped });
            return;
        }

        var responseType = context?.ResponseType ?? options.DefaultResponseType;
        var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var capture = new ResponseBodyCapture(response.Content, source, limit, (prefix, truncated, end) =>
        {
            try
            {
                var body = end is CaptureEnd.Failed or CaptureEnd.Stopped
                    ? Render(prefix, truncated, responseType, options, limit, incomplete: true)
                    : Render(prefix, truncated, responseType, options, limit, incomplete: false);
                Write(entry with
                {
                    ResponseBody = body.Text,
                    ResponseBodyStatus = body.Status,
                    BodyUnmasked = entry.BodyUnmasked || body.Status == JsonBodyStatus.Raw,
                    Truncated = entry.Truncated || body.Truncated,
                });
            }
            catch (Exception ex)
            {
                ReportFailure(ex);
            }
        });
        response.Content = capture.CreateContent();
        capture.Start();
    }

    private void Write(in JsonBodyLogEntry entry) => _sink.Write(entry);

    private async Task<Body> ReadRequestAsync(HttpContent? content, Type? modelType, JsonBodyLoggingOptions options, int limit)
    {
        if (content is null)
        {
            return new Body(null, JsonBodyStatus.None, false);
        }

        if (SkipReason(content.Headers, limit) is { } reason)
        {
            return new Body($"[body not logged: {reason}]", JsonBodyStatus.Skipped, false);
        }

        var captured = new ArrayBufferWriter<byte>(Math.Min(limit + 1, 4096));
        if (content is StreamContent)
        {
            var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
            if (!stream.CanSeek)
            {
                return new Body("[body not buffered]", JsonBodyStatus.NotBuffered, false);
            }

            var position = stream.Position;
            try
            {
                stream.Position = 0;
                while (captured.WrittenCount <= limit)
                {
                    var memory = captured.GetMemory();
                    var read = await stream.ReadAsync(memory[..Math.Min(memory.Length, limit + 1 - captured.WrittenCount)]).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    captured.Advance(read);
                }
            }
            finally
            {
                stream.Position = position;
            }
        }
        else
        {
            try
            {
                await content.CopyToAsync(new CappedStream(captured, limit + 1)).ConfigureAwait(false);
            }
            catch (CappedStream.FullException)
            {
            }
            catch (Exception)
            {
                return new Body("[body not buffered]", JsonBodyStatus.NotBuffered, false);
            }
        }

        var truncated = captured.WrittenCount > limit;
        return Render(captured.WrittenSpan[..Math.Min(captured.WrittenCount, limit)], truncated, modelType, options, limit, incomplete: false);
    }

    private Body Render(ReadOnlySpan<byte> utf8, bool truncated, Type? modelType, JsonBodyLoggingOptions options, int limit, bool incomplete)
    {
        if (utf8.IsEmpty && !truncated && !incomplete)
        {
            return new Body(null, JsonBodyStatus.None, false);
        }

        if (options.IncludeSensitive)
        {
            return new Body(Encoding.UTF8.GetString(utf8), incomplete ? JsonBodyStatus.Incomplete : JsonBodyStatus.Raw, truncated);
        }

        MaskResult result;
        var output = new ArrayBufferWriter<byte>(Math.Max(utf8.Length, 1));
        try
        {
            var masker = _maskerProvider is null ? DefaultMasker : _maskerProvider.GetMasker(modelType, _clientName);
            if (masker is null)
            {
                return new Body("[body withheld]", JsonBodyStatus.Withheld, truncated);
            }

            result = masker.Mask(utf8, output, new JsonObserverOptions(MaxOutputBytes: Math.Max(limit, 1)));
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return new Body("[body not logged: masking failed]", JsonBodyStatus.Failed, truncated);
        }

        var status = incomplete
            ? JsonBodyStatus.Incomplete
            : result.Status switch
            {
                MaskStatus.Masked => truncated ? JsonBodyStatus.Truncated : JsonBodyStatus.Masked,
                MaskStatus.Truncated => JsonBodyStatus.Truncated,
                MaskStatus.NotJson => JsonBodyStatus.NotJson,
                _ => JsonBodyStatus.Invalid,
            };

        var text = output.WrittenCount > 0
            ? Encoding.UTF8.GetString(output.WrittenSpan)
            : status switch
            {
                JsonBodyStatus.NotJson => "[body not JSON]",
                JsonBodyStatus.Incomplete => "[body incomplete]",
                JsonBodyStatus.Truncated => "[body truncated]",
                _ => "[invalid JSON]",
            };

        return new Body(text, status, truncated || result.Status == MaskStatus.Truncated);
    }

    private static string? SkipReason(HttpContentHeaders headers, int limit)
    {
        var contentType = headers.ContentType;
        var mediaType = contentType?.MediaType;
        if (!string.IsNullOrEmpty(mediaType)
            && !mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            && !mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase)
            && !mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
        {
            return mediaType;
        }

        var charset = contentType?.CharSet?.Trim('"');
        if (!string.IsNullOrEmpty(charset)
            && !charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)
            && !charset.Equals("utf8", StringComparison.OrdinalIgnoreCase)
            && !charset.Equals("us-ascii", StringComparison.OrdinalIgnoreCase))
        {
            return $"charset {charset}";
        }

        foreach (var encoding in headers.ContentEncoding)
        {
            if (!encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
            {
                return $"encoding {encoding}";
            }
        }

        return limit == 0 ? "MaxBodyBytes is 0" : null;
    }

    private static string PathOf(Uri? uri)
    {
        if (uri is null)
        {
            return string.Empty;
        }

        if (uri.IsAbsoluteUri)
        {
            return uri.AbsolutePath;
        }

        var original = uri.OriginalString;
        var end = original.IndexOfAny(['?', '#']);
        return end < 0 ? original : original[..end];
    }

    private void ReportFailure(Exception exception)
    {
        try
        {
            var level = Interlocked.Exchange(ref _failureReported, 1) == 0 ? LogLevel.Warning : LogLevel.Debug;
            _logger.Log(level, exception, "Failed to log HTTP body for client {ClientName}", _clientName);
        }
        catch
        {
            // A broken logger must not replace the caller's response.
        }
    }

    private readonly record struct Body(string? Text, JsonBodyStatus Status, bool Truncated);

    private sealed class CappedStream(ArrayBufferWriter<byte> target, int capacity) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => target.WrittenCount;

        public override long Position
        {
            get => target.WrittenCount;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var take = Math.Min(buffer.Length, capacity - target.WrittenCount);
            target.Write(buffer[..take]);
            if (target.WrittenCount >= capacity)
            {
                throw new FullException();
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        internal sealed class FullException : Exception;
    }
}
