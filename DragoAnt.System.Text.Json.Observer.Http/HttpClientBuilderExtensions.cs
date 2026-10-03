using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DragoAnt.System.Text.Json.Observer.Http;

public static class HttpClientBuilderExtensions
{
    public static IHttpClientBuilder AddJsonBodyLogging(
        this IHttpClientBuilder builder,
        Action<JsonBodyLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure != null)
        {
            builder.Services.Configure(builder.Name, configure);
        }

        builder.AddHttpMessageHandler(sp =>
        {
            var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<JsonBodyLoggingOptions>>();
            var maskerProvider = sp.GetService<IJsonBodyMaskerProvider>();
            var sink = sp.GetService<IJsonBodyLogSink>();
            var logger = sp.GetService<ILogger<JsonBodyLoggingHandler>>();

            return new JsonBodyLoggingHandler(builder.Name, optionsMonitor, maskerProvider, sink, logger);
        });

        return builder;
    }
}
