using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// Registers <see cref="JsonBodyLoggingHandler"/> on an <see cref="IHttpClientBuilder"/>.
/// </summary>
public static class HttpClientBuilderExtensions
{
    /// <summary>
    /// Adds JSON body logging to the client. An <see cref="IJsonBodyMaskerProvider"/>, an <see cref="IJsonBodyLogSink"/> and an
    /// <see cref="ILogger{TCategoryName}"/> registered in DI are used when present.
    /// </summary>
    /// <param name="builder">The client builder.</param>
    /// <param name="configure">Configures the options named after the client; option changes apply to the next call.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>Calling it again for the same client only applies <paramref name="configure"/>; the handler is added once.</remarks>
    public static IHttpClientBuilder AddJsonBodyLogging(
        this IHttpClientBuilder builder,
        Action<JsonBodyLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure != null)
        {
            builder.Services.Configure(builder.Name, configure);
        }

        var registration = new Registration(builder.Name);
        if (builder.Services.Any(d => d.ServiceType == typeof(Registration) && registration.Equals(d.ImplementationInstance)))
        {
            return builder;
        }

        builder.Services.AddSingleton(registration);
        builder.AddHttpMessageHandler(sp => new JsonBodyLoggingHandler(
            builder.Name,
            sp.GetRequiredService<IOptionsMonitor<JsonBodyLoggingOptions>>(),
            sp.GetService<IJsonBodyMaskerProvider>(),
            sp.GetService<IJsonBodyLogSink>(),
            sp.GetService<ILogger<JsonBodyLoggingHandler>>()));

        return builder;
    }

    private sealed record Registration(string Name);
}
