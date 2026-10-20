namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// One logged HTTP call, handed to an <see cref="IJsonBodyLogSink"/>.
/// </summary>
public readonly record struct JsonBodyLogEntry
{
    /// <summary>
    /// Name of the <see cref="HttpClient"/> the call was made with; empty for a handler created without a name.
    /// </summary>
    public string ClientName { get; init; }

    /// <summary>
    /// Operation name passed to <see cref="JsonBodyLogging.WithBodyLogging{TRequest,TResponse}"/>, or <c>null</c>.
    /// </summary>
    public string? Operation { get; init; }

    /// <summary>
    /// HTTP method of the request.
    /// </summary>
    public HttpMethod Method { get; init; }

    /// <summary>
    /// Request path without the query string and fragment. The query string is never logged because it often carries secrets.
    /// </summary>
    public string Path { get; init; }

    /// <summary>
    /// Response status code, or <c>null</c> when no response arrived.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Milliseconds from sending the request until the response headers arrived or the call failed.
    /// </summary>
    public double ElapsedMs { get; init; }

    /// <summary>
    /// How the call ended.
    /// </summary>
    public JsonBodyOutcome Outcome { get; init; }

    /// <summary>
    /// Masked request body, a bracketed marker (see <see cref="RequestBodyStatus"/>), or <c>null</c> when there is no body.
    /// </summary>
    public string? RequestBody { get; init; }

    /// <summary>
    /// What <see cref="RequestBody"/> contains.
    /// </summary>
    public JsonBodyStatus RequestBodyStatus { get; init; }

    /// <summary>
    /// Masked response body, a bracketed marker (see <see cref="ResponseBodyStatus"/>), or <c>null</c> when there is no response or no body.
    /// </summary>
    public string? ResponseBody { get; init; }

    /// <summary>
    /// What <see cref="ResponseBody"/> contains.
    /// </summary>
    public JsonBodyStatus ResponseBodyStatus { get; init; }

    /// <summary>
    /// <c>true</c> when a body was logged unmasked because <see cref="JsonBodyLoggingOptions.IncludeSensitive"/> is on.
    /// </summary>
    public bool BodyUnmasked { get; init; }

    /// <summary>
    /// <c>true</c> when a body was longer than <see cref="JsonBodyLoggingOptions.MaxBodyBytes"/> and only its start is logged.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// The exception the call failed with, or <c>null</c>. It is rethrown to the caller unchanged.
    /// </summary>
    public Exception? Exception { get; init; }
}
