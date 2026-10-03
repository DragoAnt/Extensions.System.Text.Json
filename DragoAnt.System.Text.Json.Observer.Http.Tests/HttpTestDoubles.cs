using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.Tests;

internal static class HttpTestDoubles
{
    public static JsonBodyLoggingHandler Handler(
        JsonBodyLoggingOptions options,
        IJsonBodyLogSink sink,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> inner,
        IJsonBodyMaskerProvider? maskerProvider = null,
        ILogger<JsonBodyLoggingHandler>? logger = null) =>
        new(options, maskerProvider, sink, logger) { InnerHandler = new FuncHandler(inner) };

    public static JsonBodyLoggingHandler Handler(
        JsonBodyLoggingOptions options,
        IJsonBodyLogSink sink,
        Func<HttpRequestMessage, HttpResponseMessage> inner,
        IJsonBodyMaskerProvider? maskerProvider = null,
        ILogger<JsonBodyLoggingHandler>? logger = null) =>
        Handler(options, sink, (request, _) => Task.FromResult(inner(request)), maskerProvider, logger);

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");
}

internal sealed class FuncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}

internal sealed class CapturingSink : IJsonBodyLogSink
{
    private readonly ConcurrentQueue<JsonBodyLogEntry> _entries = new();
    private readonly SemaphoreSlim _written = new(0);

    public Func<JsonBodyLogEntry, bool>? Throw { get; init; }

    public IReadOnlyList<JsonBodyLogEntry> Entries => _entries.ToArray();

    public void Write(in JsonBodyLogEntry entry)
    {
        if (Throw?.Invoke(entry) == true)
        {
            throw new InvalidOperationException("sink failure");
        }

        _entries.Enqueue(entry);
        _written.Release();
    }

    public async Task<JsonBodyLogEntry> WaitSingleAsync(int timeoutMs = 5000)
    {
        Assert.True(await _written.WaitAsync(timeoutMs), "no log entry was written");
        return Assert.Single(_entries);
    }
}

internal sealed class FuncMaskerProvider(Func<Type?, string, JsonObserver?> getMasker) : IJsonBodyMaskerProvider
{
    public JsonObserver? GetMasker(Type? modelType, string clientName) => getMasker(modelType, clientName);
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _records = new();

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Records => _records.ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _records.Enqueue((logLevel, formatter(state, exception), exception));
}

/// <summary>Yields the given chunks one by one, then blocks until cancelled, ends, or fails as configured.</summary>
internal sealed class ScriptedStream(IReadOnlyList<byte[]> chunks, ScriptedStream.Tail tail) : Stream
{
    public enum Tail
    {
        End,
        Hang,
        Fail,
    }

    private int _chunk;
    private int _offset;

    public bool Disposed { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_chunk < chunks.Count)
        {
            var current = chunks[_chunk];
            var count = Math.Min(buffer.Length, current.Length - _offset);
            current.AsSpan(_offset, count).CopyTo(buffer.Span);
            _offset += count;
            if (_offset == current.Length)
            {
                _chunk++;
                _offset = 0;
            }

            return count;
        }

        switch (tail)
        {
            case Tail.Hang:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            case Tail.Fail:
                throw new IOException("connection reset");
            default:
                return 0;
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

public sealed record HttpTestRequest(string Name, string Secret);

public sealed record HttpTestResponse(int Id, string Token);
