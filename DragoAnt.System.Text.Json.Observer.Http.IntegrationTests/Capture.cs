using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.IntegrationTests;

internal sealed class CapturingSink : IJsonBodyLogSink
{
    private readonly ConcurrentQueue<JsonBodyLogEntry> _entries = new();
    private readonly SemaphoreSlim _written = new(0);

    public IReadOnlyList<JsonBodyLogEntry> Entries => _entries.ToArray();

    public void Write(in JsonBodyLogEntry entry)
    {
        _entries.Enqueue(entry);
        _written.Release();
    }

    public async Task<IReadOnlyList<JsonBodyLogEntry>> WaitAsync(int count, int timeoutMs = 5_000)
    {
        while (_entries.Count < count)
        {
            Assert.True(await _written.WaitAsync(timeoutMs), $"expected {count} entries, found {_entries.Count}");
        }

        return Entries;
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _records = new();
    private readonly SemaphoreSlim _written = new(0);

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Records => _records.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(_records, _written);

    public void Dispose() => _written.Dispose();

    public async Task<IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)>> WaitAsync(int count, int timeoutMs = 5_000)
    {
        while (_records.Count < count)
        {
            Assert.True(await _written.WaitAsync(timeoutMs), $"expected {count} log records, found {_records.Count}");
        }

        return Records;
    }

    private sealed class Logger(
        ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> records,
        SemaphoreSlim written) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            records.Enqueue((logLevel, formatter(state, exception), exception));
            written.Release();
        }
    }
}
