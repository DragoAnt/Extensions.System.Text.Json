using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DragoAnt.System.Text.Json.Observer.Http;

public sealed class LoggerJsonBodyLogSink : IJsonBodyLogSink
{
    private readonly ILogger _logger;

    public LoggerJsonBodyLogSink(ILogger<JsonBodyLoggingHandler> logger)
    {
        _logger = logger ?? NullLogger<JsonBodyLoggingHandler>.Instance;
    }

    public LoggerJsonBodyLogSink(ILogger logger)
    {
        _logger = logger ?? NullLogger.Instance;
    }

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
            "{ClientName} {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms: outcome={Outcome} requestBody={RequestBody} responseBody={ResponseBody}",
            entry.ClientName,
            entry.Method,
            entry.Path,
            entry.StatusCode,
            entry.ElapsedMs,
            entry.Outcome,
            entry.RequestBody,
            entry.ResponseBody);
    }
}
