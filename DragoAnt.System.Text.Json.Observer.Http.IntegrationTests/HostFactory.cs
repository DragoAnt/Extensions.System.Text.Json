using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DragoAnt.System.Text.Json.Observer.Http.IntegrationTests;

public sealed class HostFactory : WebApplicationFactory<Program>
{
    public Gate Gate => Services.GetRequiredService<Gate>();

    public HttpClient CreateDirectClient(
        JsonBodyLoggingOptions options,
        IJsonBodyLogSink? sink = null,
        IJsonBodyMaskerProvider? provider = null,
        ILogger<JsonBodyLoggingHandler>? logger = null) =>
        CreateDefaultClient(
            new JsonBodyLoggingHandler(options, provider, sink, logger),
            new TestServerLifetimeHandler());

    public FactoryClient CreateFactoryClient(
        Action<JsonBodyLoggingOptions> configure,
        Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        register?.Invoke(services);
        services.AddHttpClient("api", client => client.BaseAddress = Server.BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => new TestServerLifetimeHandler { InnerHandler = Server.CreateHandler() })
            .AddJsonBodyLogging(configure);
        var serviceProvider = services.BuildServiceProvider();
        return new FactoryClient(serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("api"), serviceProvider);
    }
}

internal sealed class TestServerLifetimeHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var response = await base.SendAsync(request, lifetime.Token);
            var path = request.RequestUri?.AbsolutePath;
            if (path == "/slow" && (int)response.StatusCode == 499 && cancellationToken.IsCancellationRequested)
            {
                response.Dispose();
                throw new TaskCanceledException("The test server request was canceled.", null, cancellationToken);
            }

            var stripContentLength = path is "/chunked" or "/stream-json" or "/sse";
            response.Content = new LifetimeContent(response.Content, lifetime, stripContentLength);
            return response;
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    private sealed class LifetimeContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly CancellationTokenSource _lifetime;
        private readonly bool _stripContentLength;

        public LifetimeContent(HttpContent inner, CancellationTokenSource lifetime, bool stripContentLength)
        {
            _inner = inner;
            _lifetime = lifetime;
            _stripContentLength = stripContentLength;
            foreach (var header in inner.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (stripContentLength)
            {
                Headers.ContentLength = null;
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => _inner.CopyToAsync(stream);

        protected override Task<Stream> CreateContentReadStreamAsync()
            => _inner.ReadAsStreamAsync();

        protected override bool TryComputeLength(out long length)
        {
            if (!_stripContentLength && _inner.Headers.ContentLength is { } contentLength)
            {
                length = contentLength;
                return true;
            }

            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _lifetime.Cancel();
                _inner.Dispose();
                _lifetime.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

public sealed class FactoryClient(HttpClient client, ServiceProvider services) : IDisposable
{
    public HttpClient Client { get; } = client;

    public ServiceProvider Services { get; } = services;

    public void Dispose()
    {
        Client.Dispose();
        Services.Dispose();
    }
}
