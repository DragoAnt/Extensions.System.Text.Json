using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// The default <see cref="IJsonBodyLogSink"/>: writes each entry as one structured <see cref="ILogger"/> message,
/// at <see cref="LogLevel.Information"/> for a successful call and <see cref="LogLevel.Warning"/> otherwise.
/// </summary>
/// <remarks>
/// Every field of <see cref="JsonBodyLogEntry"/> except <see cref="JsonBodyLogEntry.Exception"/> is a named template property
/// (<c>ClientName</c>, <c>Operation</c>, <c>Method</c>, <c>Path</c>, <c>StatusCode</c>, <c>ElapsedMs</c>, <c>Outcome</c>,
/// <c>Truncated</c>, <c>BodyUnmasked</c>, <c>RequestBodyStatus</c>, <c>RequestBody</c>, <c>ResponseBodyStatus</c>, <c>ResponseBody</c>);
/// the exception is attached to the message.
/// </remarks>
public sealed class LoggerJsonBodyLogSink : IJsonBodyLogSink
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a sink that writes to <paramref name="logger"/>.
    /// </summary>
    /// <param name="logger">Target logger; when <c>null</c>, nothing is written.</param>
    public LoggerJsonBodyLogSink(ILogger? logger)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public void Write(in JsonBodyLogEntry entry)
    {
        var level = entry.Outcome == JsonBodyOutcome.Success ? LogLevel.Information : LogLevel.Warning;
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        _logger.Log(
            level,
            entry.Exception,
            "HTTP {ClientName} {Operation} {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms: outcome={Outcome} truncated={Truncated} unmasked={BodyUnmasked} request({RequestBodyStatus})={RequestBody} response({ResponseBodyStatus})={ResponseBody}",
            entry.ClientName,
            entry.Operation,
            entry.Method,
            entry.Path,
            entry.StatusCode,
            entry.ElapsedMs,
            entry.Outcome,
            entry.Truncated,
            entry.BodyUnmasked,
            entry.RequestBodyStatus,
            entry.RequestBody,
            entry.ResponseBodyStatus,
            entry.ResponseBody);
    }
}
