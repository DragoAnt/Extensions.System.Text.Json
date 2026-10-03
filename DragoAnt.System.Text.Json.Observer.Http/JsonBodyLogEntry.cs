namespace DragoAnt.System.Text.Json.Observer.Http;

public readonly record struct JsonBodyLogEntry(
    string ClientName,
    HttpMethod Method,
    string Path,
    int? StatusCode,
    double ElapsedMs,
    JsonBodyOutcome Outcome,
    string? RequestBody,
    string? ResponseBody,
    bool BodyUnmasked,
    bool Truncated,
    Exception? Exception);
