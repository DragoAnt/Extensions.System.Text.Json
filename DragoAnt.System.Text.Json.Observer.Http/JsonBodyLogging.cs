namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// Per-request extensions that tell <see cref="JsonBodyLoggingHandler"/> the body model types and the operation name.
/// </summary>
public static class JsonBodyLogging
{
    /// <summary>
    /// Key of the <see cref="JsonBodyLoggingContext"/> in <see cref="HttpRequestMessage.Options"/>.
    /// </summary>
    public static readonly HttpRequestOptionsKey<JsonBodyLoggingContext> Key =
        new("DragoAnt.System.Text.Json.Observer.Http.Context");

    /// <summary>
    /// Attaches the body model types and an operation name to the request.
    /// </summary>
    /// <typeparam name="TRequest">Model type of the request body.</typeparam>
    /// <typeparam name="TResponse">Model type of the response body.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="operation">Operation name written to <see cref="JsonBodyLogEntry.Operation"/>.</param>
    /// <returns>The same request, for chaining.</returns>
    public static HttpRequestMessage WithBodyLogging<TRequest, TResponse>(
        this HttpRequestMessage request,
        string? operation = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(Key, new JsonBodyLoggingContext(typeof(TRequest), typeof(TResponse), operation));
        return request;
    }

    /// <summary>
    /// Attaches the body model types and an operation name to the request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="requestType">Model type of the request body, or <c>null</c>.</param>
    /// <param name="responseType">Model type of the response body, or <c>null</c>.</param>
    /// <param name="operation">Operation name written to <see cref="JsonBodyLogEntry.Operation"/>.</param>
    /// <returns>The same request, for chaining.</returns>
    public static HttpRequestMessage WithBodyLogging(
        this HttpRequestMessage request,
        Type? requestType = null,
        Type? responseType = null,
        string? operation = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(Key, new JsonBodyLoggingContext(requestType, responseType, operation));
        return request;
    }

    /// <summary>
    /// Attaches the body model types and an operation name to the request, then sends it.
    /// </summary>
    /// <typeparam name="TRequest">Model type of the request body.</typeparam>
    /// <typeparam name="TResponse">Model type of the response body.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="request">The request.</param>
    /// <param name="operation">Operation name written to <see cref="JsonBodyLogEntry.Operation"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response.</returns>
    public static Task<HttpResponseMessage> SendWithBodyLoggingAsync<TRequest, TResponse>(
        this HttpClient client,
        HttpRequestMessage request,
        string? operation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.SendAsync(request.WithBodyLogging<TRequest, TResponse>(operation), cancellationToken);
    }
}
