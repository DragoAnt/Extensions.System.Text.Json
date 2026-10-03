namespace DragoAnt.System.Text.Json.Observer.Http;

public static class JsonBodyLogging
{
    public static readonly HttpRequestOptionsKey<JsonBodyLoggingContext> Key =
        new("DragoAnt.System.Text.Json.Observer.Http.Context");

    public static HttpRequestMessage WithBodyLogging<TRequest, TResponse>(
        this HttpRequestMessage request,
        string? operation = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(Key, new JsonBodyLoggingContext(typeof(TRequest), typeof(TResponse), operation));
        return request;
    }

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

    public static async Task<HttpResponseMessage> SendWithBodyLoggingAsync<TRequest, TResponse>(
        this HttpClient client,
        HttpRequestMessage request,
        string? operation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        request.WithBodyLogging<TRequest, TResponse>(operation);
        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
