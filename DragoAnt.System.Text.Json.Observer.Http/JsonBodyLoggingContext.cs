namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// Per-request logging details, attached with <see cref="JsonBodyLogging.WithBodyLogging{TRequest,TResponse}"/> and stored under
/// <see cref="JsonBodyLogging.Key"/>.
/// </summary>
/// <param name="RequestType">Model type of the request body, passed to <see cref="IJsonBodyMaskerProvider.GetMasker"/>.</param>
/// <param name="ResponseType">Model type of the response body, passed to <see cref="IJsonBodyMaskerProvider.GetMasker"/>.</param>
/// <param name="Operation">Operation name written to <see cref="JsonBodyLogEntry.Operation"/>.</param>
public sealed record JsonBodyLoggingContext(Type? RequestType, Type? ResponseType, string? Operation = null);
