using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.Tests;

public class JsonBodyLoggingTests
{
    [Fact]
    public void Options_Defaults_AreExpected()
    {
        var options = new JsonBodyLoggingOptions();
        options.When.Should().Be(JsonBodyLogWhen.OnFailure);
        options.MaxBodyBytes.Should().Be(4096);
        options.IncludeSensitive.Should().BeFalse();
        options.DefaultRequestType.Should().BeNull();
        options.DefaultResponseType.Should().BeNull();
    }

    [Fact]
    public void WithBodyLogging_SetsOptionsKey()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/test");
        request.WithBodyLogging<TestRequest, TestResponse>("CreateItem");

        request.Options.TryGetValue(JsonBodyLogging.Key, out var context).Should().BeTrue();
        context.Should().NotBeNull();
        context!.RequestType.Should().Be(typeof(TestRequest));
        context.ResponseType.Should().Be(typeof(TestResponse));
        context.Operation.Should().Be("CreateItem");
    }

    [Fact]
    public async Task OnFailure_Success_LogsNothing()
    {
        var sink = new TestSink();
        var handler = CreateHandler(new JsonBodyLoggingOptions { When = JsonBodyLogWhen.OnFailure }, sink, _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok"}""", Encoding.UTF8, "application/json")
            });

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("https://example.com/api/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sink.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task OnFailure_500_LogsMaskedBoth()
    {
        var sink = new TestSink();
        var masker = JsonObserver.Obj(rules => rules.Match("secret").MaskStr("*****"));
        var provider = new TestMaskerProvider { GetMaskerFunc = (_, _) => masker };

        var handler = CreateHandler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.OnFailure },
            sink,
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("""{"secret":"server_pw","error":"db_failed"}""", Encoding.UTF8, "application/json")
            },
            maskerProvider: provider);

        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/submit")
        {
            Content = new StringContent("""{"secret":"client_pw","value":123}""", Encoding.UTF8, "application/json")
        };
        request.WithBodyLogging<TestRequest, TestResponse>("Submit");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        sink.Entries.Should().HaveCount(1);

        var entry = sink.Entries[0];
        entry.Outcome.Should().Be(JsonBodyOutcome.Failure);
        entry.StatusCode.Should().Be(500);
        entry.RequestBody.Should().NotContain("client_pw");
        entry.RequestBody.Should().Contain("*****");
        entry.ResponseBody.Should().NotContain("server_pw");
        entry.ResponseBody.Should().Contain("*****");
    }

    [Fact]
    public async Task Exception_LogsRequestAndRethrows()
    {
        var sink = new TestSink();
        var handler = CreateHandler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.OnFailure },
            sink,
            _ => throw new HttpRequestException("Network failure"));

        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/fail")
        {
            Content = new StringContent("""{"id":1}""", Encoding.UTF8, "application/json")
        };

        var act = async () => await client.SendAsync(request);
        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Network failure");

        sink.Entries.Should().HaveCount(1);
        var entry = sink.Entries[0];
        entry.Outcome.Should().Be(JsonBodyOutcome.Exception);
        entry.Exception.Should().BeOfType<HttpRequestException>();
        entry.StatusCode.Should().BeNull();
        entry.RequestBody.Should().NotBeNullOrEmpty();
        entry.RequestBody.Should().Contain("***");
    }

    [Fact]
    public async Task Always_200_Logs()
    {
        var sink = new TestSink();
        var handler = CreateHandler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":"test"}""", Encoding.UTF8, "application/json")
            });

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("https://example.com/api/always");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].Outcome.Should().Be(JsonBodyOutcome.Success);
        sink.Entries[0].StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Never_LogsNothing()
    {
        var sink = new TestSink();
        var handler = CreateHandler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Never },
            sink,
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"bad"}""", Encoding.UTF8, "application/json")
            });

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("https://example.com/api/never");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        sink.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task LargeResponse_CallerReadsFullBody()
    {
        var sink = new TestSink();
        // 10 KB string response with MaxBodyBytes = 100
        var fullPayload = new string('x', 10000);
        var json = $"{{\"content\":\"{fullPayload}\"}}";

        var handler = CreateHandler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always, MaxBodyBytes = 100 },
            sink,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("https://example.com/api/large");

        // Caller must still be able to read the entire full body!
        var readByCaller = await response.Content.ReadAsStringAsync();
        readByCaller.Should().Be(json);
        readByCaller.Length.Should().Be(json.Length);

        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task LargeResponse_LoggedTruncated()
    {
        var sink = new TestSink();
        var largeJson = "{\"field\":\"" + new string('A', 5000) + "\"}";

        var handler = CreateHandler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always, MaxBodyBytes = 64 },
            sink,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(largeJson, Encoding.UTF8, "application/json")
            });

        using var client = new HttpClient(handler);
        await client.GetAsync("https://example.com/api/trunc");

        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task StreamContent_NotBuffered()
    {
        var sink = new TestSink();
        var handler = CreateHandler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => new HttpResponseMessage(HttpStatusCode.OK));

        using var client = new HttpClient(handler);
        // Custom non-seekable stream content
        using var unseekableStream = new NonSeekableStream(Encoding.UTF8.GetBytes("""{"unseekable":true}"""));
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/stream")
        {
            Content = new StreamContent(unseekableStream)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        await client.SendAsync(request);

        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].RequestBody.Should().Be("[body not buffered]");
    }

    [Fact]
    public async Task ResponseHeadersRead_Streaming_Preserved()
    {
        var sink = new TestSink();
        var json = """{"stream":123,"nested":{"items":[1,2,3]}}""";

        var handler = CreateHandler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api/stream-headers");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        // Caller reads stream incrementally
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var readJson = await reader.ReadToEndAsync();

        readJson.Should().Be(json);
        sink.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task NoContext_UsesClientDefault()
    {
        var sink = new TestSink();
        var passedTypes = new List<Type?>();
        var provider = new TestMaskerProvider
        {
            GetMaskerFunc = (type, _) =>
            {
                passedTypes.Add(type);
                return JsonObserver.Obj(rules => rules.Match("id").MaskStr("***"));
            }
        };

        var handler = CreateHandler(
            new JsonBodyLoggingOptions
            {
                When = JsonBodyLogWhen.Always,
                DefaultRequestType = typeof(TestRequest)
            },
            sink,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":99}""", Encoding.UTF8, "application/json")
            },
            maskerProvider: provider);

        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/nodefault")
        {
            Content = new StringContent("""{"id":42}""", Encoding.UTF8, "application/json")
        };
        // Do NOT call WithBodyLogging

        await client.SendAsync(request);

        passedTypes.Should().Contain(typeof(TestRequest));
        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].RequestBody.Should().Contain("***");
    }

    [Fact]
    public async Task NonJson_ContentType_LoggedAsType()
    {
        var sink = new TestSink();
        var handler = CreateHandler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("plain text response", Encoding.UTF8, "text/plain")
            });

        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/text")
        {
            Content = new StringContent("plain text request", Encoding.UTF8, "text/plain")
        };

        await client.SendAsync(request);

        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].RequestBody.Should().Be("[body not logged: text/plain]");
        sink.Entries[0].ResponseBody.Should().Be("[body not logged: text/plain]");
    }

    [Fact]
    public async Task MaskerProvider_ReturnsNull_LoggedAsWithheld()
    {
        var sink = new TestSink();
        var provider = new TestMaskerProvider { GetMaskerFunc = (_, _) => null };

        var handler = CreateHandler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"secret":123}""", Encoding.UTF8, "application/json")
            },
            maskerProvider: provider);

        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/withheld")
        {
            Content = new StringContent("""{"secret":456}""", Encoding.UTF8, "application/json")
        };

        await client.SendAsync(request);

        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].RequestBody.Should().Be("[body withheld]");
        sink.Entries[0].ResponseBody.Should().Be("[body withheld]");
    }

    [Fact]
    public async Task AddJsonBodyLogging_NamedClient_GetsOwnOptions()
    {
        var services = new ServiceCollection();
        var sink = new TestSink();
        services.AddSingleton<IJsonBodyLogSink>(sink);

        services.AddHttpClient("ClientA")
            .AddJsonBodyLogging(o => o.When = JsonBodyLogWhen.Always)
            .ConfigurePrimaryHttpMessageHandler(() => new TestMockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        services.AddHttpClient("ClientB")
            .AddJsonBodyLogging(o => o.When = JsonBodyLogWhen.Never)
            .ConfigurePrimaryHttpMessageHandler(() => new TestMockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var clientA = factory.CreateClient("ClientA");
        var clientB = factory.CreateClient("ClientB");

        await clientA.GetAsync("https://example.com/api/a");
        await clientB.GetAsync("https://example.com/api/b");

        sink.Entries.Should().HaveCount(1);
        sink.Entries[0].ClientName.Should().Be("ClientA");
    }

    private static JsonBodyLoggingHandler CreateHandler(
        JsonBodyLoggingOptions options,
        IJsonBodyLogSink sink,
        Func<HttpRequestMessage, HttpResponseMessage> handlerFunc,
        IJsonBodyMaskerProvider? maskerProvider = null)
    {
        return CreateHandler(options, sink, (req, _) => Task.FromResult(handlerFunc(req)), maskerProvider);
    }

    private static JsonBodyLoggingHandler CreateHandler(
        JsonBodyLoggingOptions options,
        IJsonBodyLogSink sink,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc,
        IJsonBodyMaskerProvider? maskerProvider = null)
    {
        var testHandler = new TestMockHttpMessageHandler(handlerFunc);
        var loggingHandler = new JsonBodyLoggingHandler(options, maskerProvider, sink)
        {
            InnerHandler = testHandler
        };
        return loggingHandler;
    }

    private sealed class TestMockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public TestMockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public TestMockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = (req, _) => Task.FromResult(handler(req));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed class TestSink : IJsonBodyLogSink
    {
        public List<JsonBodyLogEntry> Entries { get; } = new();

        public void Write(in JsonBodyLogEntry entry)
        {
            Entries.Add(entry);
        }
    }

    private sealed class TestMaskerProvider : IJsonBodyMaskerProvider
    {
        public Func<Type?, string, JsonObserver?> GetMaskerFunc { get; set; } = (_, _) => null;

        public JsonObserver? GetMasker(Type? modelType, string clientName) =>
            GetMaskerFunc(modelType, clientName);
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] buffer) : base(buffer) { }

        public override bool CanSeek => false;
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }

    private sealed record TestRequest(string Name, string Secret);
    private sealed record TestResponse(int Id, string Token);
}
