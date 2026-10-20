namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// Per-request logging details, attached with <see cref="JsonBodyLogging.WithBodyLogging{TRequest,TResponse}"/> and stored under
/// <see cref="JsonBodyLogging.Key"/>.
/// </summary>
public sealed record JsonBodyLoggingContext
{
    /// <summary>
    /// Model type of the request body, passed to <see cref="IJsonBodyMaskerProvider.GetMasker"/>.
    /// </summary>
    public Type? RequestType { get; init; }

    /// <summary>
    /// Model type of the response body, passed to <see cref="IJsonBodyMaskerProvider.GetMasker"/>.
    /// </summary>
    public Type? ResponseType { get; init; }

    /// <summary>
    /// Operation name written to <see cref="JsonBodyLogEntry.Operation"/>.
    /// </summary>
    public string? Operation { get; init; }
}
