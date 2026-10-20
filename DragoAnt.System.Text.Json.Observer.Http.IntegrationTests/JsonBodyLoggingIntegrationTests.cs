using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.IntegrationTests;

public enum Wiring
{
    Direct,
    Factory,
}

public sealed class JsonBodyLoggingIntegrationTests
{
    private static readonly string HashKey = Convert.ToBase64String(
        Enumerable.Range(0, 64).Select(i => (byte)(i * 7 + 3)).ToArray());

    [Theory]
    [InlineData(Wiring.Direct)]
    [InlineData(Wiring.Factory)]
    public async Task Echo_RequestAndResponseMasked_CallerGetsOriginal(Wiring wiring)
    {
        await using var factory = new HostFactory();
        var masker = JsonObserver.Obj(
            JsonValuePolicy.AnyDepth(rules => rules.Match("password").Mask("***"), ValuePolicy.BlockList));
        using var harness = Create(
            factory,
            wiring,
            options => options.When = JsonBodyLogWhen.Always,
            masker,
            defaultLogger: wiring == Wiring.Factory);
        const string json = """{"user":"u","password":"pw-1"}""";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await harness.Client.PostAsync("/echo", content);
        var callerBody = await response.Content.ReadAsStringAsync();

        callerBody.Should().Be(json);
        if (wiring == Wiring.Factory)
        {
            var message = (await harness.Logger!.WaitAsync(5))
                .Should().ContainSingle(item => item.Message.StartsWith("HTTP api", StringComparison.Ordinal)).Which.Message;
            message.Should().NotContain("pw-1")
                .And.Contain("\"user\":\"u\"")
                .And.Contain("request(Masked)")
                .And.Contain("response(Masked)");
        }
        else
        {
            var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;
            entry.RequestBody.Should().NotContain("pw-1");
            entry.ResponseBody.Should().NotContain("pw-1");
            entry.RequestBodyStatus.Should().Be(JsonBodyStatus.Masked);
            entry.ResponseBodyStatus.Should().Be(JsonBodyStatus.Masked);
        }
    }

    [Theory]
    [InlineData(Wiring.Direct)]
    [InlineData(Wiring.Factory)]
    public async Task Fail500_OnFailure_LoggedMasked(Wiring wiring)
    {
        await using var factory = new HostFactory();
        var masker = JsonObserver.Obj(
            JsonValuePolicy.AnyDepth(rules => rules.Match("secret").Mask("***"), ValuePolicy.BlockList));
        using var harness = Create(factory, wiring, options => options.When = JsonBodyLogWhen.OnFailure, masker);

        using var response = await harness.Client.GetAsync("/fail");
        var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        entry.Outcome.Should().Be(JsonBodyOutcome.Failure);
        entry.ResponseBodyStatus.Should().Be(JsonBodyStatus.Masked);
        entry.ResponseBody.Should().NotContain("pw-500");
    }

    [Fact]
    public async Task Success_OnFailure_NotLogged_ContentUnwrapped()
    {
        await using var factory = new HostFactory();
        using var harness = Create(factory, Wiring.Factory, options => options.When = JsonBodyLogWhen.OnFailure);

        using var response = await harness.Client.GetAsync("/item/7");
        var body = await response.Content.ReadAsStringAsync();
        response.Content.GetType().Name.Should().Be("LifetimeContent");
        using var failure = await harness.Client.GetAsync("/fail");
        var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;

        body.Should().Contain("\"id\":\"7\"");
        entry.Path.Should().Be("/fail");
    }

    [Theory]
    [InlineData(Wiring.Direct)]
    [InlineData(Wiring.Factory)]
    public async Task PlainSendAsync_Timeout_IsLoggedAsCanceled(Wiring wiring)
    {
        await using var factory = new HostFactory();
        using var harness = Create(factory, wiring, options => options.When = JsonBodyLogWhen.OnFailure);
        harness.Client.Timeout = TimeSpan.FromMilliseconds(200);

        Func<Task> action = async () => await harness.Client.GetAsync("/slow?ms=30000");
        await action.Should().ThrowAsync<TaskCanceledException>();
        var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;

        entry.Outcome.Should().Be(JsonBodyOutcome.Canceled);
        entry.Exception.Should().BeAssignableTo<OperationCanceledException>();
        entry.StatusCode.Should().BeNull();
    }

    [Theory]
    [InlineData(Wiring.Direct)]
    [InlineData(Wiring.Factory)]
    public async Task HttpClientTimeout_ViaHelper_LoggedAsTimeout_Integration(Wiring wiring)
    {
        await using var factory = new HostFactory();
        using var harness = Create(factory, wiring, options => options.When = JsonBodyLogWhen.OnFailure);
        harness.Client.Timeout = TimeSpan.FromMilliseconds(200);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/slow?ms=30000");

        Func<Task> action = async () => await harness.Client.SendWithBodyLoggingAsync<object, object>(request);
        await action.Should().ThrowAsync<TaskCanceledException>();
        var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;

        entry.Outcome.Should().Be(JsonBodyOutcome.Timeout);
        entry.Exception.Should().BeAssignableTo<OperationCanceledException>();
        entry.StatusCode.Should().BeNull();
    }

    [Theory]
    [InlineData(Wiring.Direct)]
    [InlineData(Wiring.Factory)]
    public async Task HugeBody_CallerReadsAll_LogTruncatedAndParseable(Wiring wiring)
    {
        await using var factory = new HostFactory();
        using var harness = Create(
            factory,
            wiring,
            options =>
            {
                options.When = JsonBodyLogWhen.Always;
                options.MaxBodyBytes = 4096;
            });

        using var response = await harness.Client.GetAsync("/huge?mb=20");
        var callerBytes = await response.Content.ReadAsByteArrayAsync();
        var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;
        var expected = Encoding.UTF8.GetBytes($$"""{"data":"{{new string('x', 20 * 1024 * 1024)}}"}""");

        Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(callerBytes))
            .Should().Be(Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(expected)));
        entry.Truncated.Should().BeTrue();
        entry.ResponseBodyStatus.Should().Be(JsonBodyStatus.Truncated);
        Encoding.UTF8.GetByteCount(entry.ResponseBody!).Should().BeLessThanOrEqualTo(4096);
        using var _ = JsonDocument.Parse(entry.ResponseBody!);
    }

    [Theory]
    [InlineData(Wiring.Direct)]
    [InlineData(Wiring.Factory)]
    public async Task QueryString_NotLoggedRaw(Wiring wiring)
    {
        await using var factory = new HostFactory();
        using var harness = Create(
            factory,
            wiring,
            options => options.When = JsonBodyLogWhen.Always,
            defaultLogger: wiring == Wiring.Factory);

        using var response = await harness.Client.GetAsync("/item/7?token=abc&api_key=def");
        await response.Content.ReadAsByteArrayAsync();
        if (wiring == Wiring.Factory)
        {
            var message = (await harness.Logger!.WaitAsync(5))
                .Should().ContainSingle(item => item.Message.StartsWith("HTTP api", StringComparison.Ordinal)).Which.Message;
            message.Should().Contain("/item/7").And.NotContain("abc").And.NotContain("def");
        }
        else
        {
            var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;
            entry.Path.Should().Be("/item/7");
            entry.ToString().Should().NotContain("abc").And.NotContain("def");
        }
    }

    [Fact]
    public async Task HashedField_EqualsHmacRedactor()
    {
        await using var factory = new HostFactory();
        var masker = JsonObserver.Obj(
            JsonValuePolicy.AnyDepth(rules => rules.Match("email").Mask(MaskTag.Hash), ValuePolicy.BlockList));
        using var harness = Create(
            factory,
            Wiring.Factory,
            options =>
            {
                options.When = JsonBodyLogWhen.Always;
                options.MaskOptions = new JsonObserverOptions { HashKeyId = 7 }.WithBase64HashKey(HashKey);
            },
            masker);
        const string json = """{"email":"a@b.test"}""";

        using var response = await harness.Client.PostAsync(
            "/hash",
            new StringContent(json, Encoding.UTF8, "application/json"));
        await response.Content.ReadAsByteArrayAsync();
        var entry = (await harness.Sink!.WaitAsync(1)).Should().ContainSingle().Which;
        var expected = Redactor().Redact("a@b.test");

        ReadEmail(entry.RequestBody!).Should().Be(expected);
        ReadEmail(entry.ResponseBody!).Should().Be(expected);
    }

    private static string ReadEmail(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("email").GetString()!;
    }

    private static HmacRedactor Redactor()
#pragma warning disable EXTEXP0002
        => new(Options.Create(new HmacRedactorOptions { Key = HashKey, KeyId = 7 }));
#pragma warning restore EXTEXP0002

    internal static ClientHarness Create(
        HostFactory factory,
        Wiring wiring,
        Action<JsonBodyLoggingOptions> configure,
        JsonObserver? masker = null,
        bool defaultLogger = false)
    {
        var provider = masker is null ? null : new StaticMaskerProvider(masker);
        if (wiring == Wiring.Direct)
        {
            var options = new JsonBodyLoggingOptions();
            configure(options);
            var sink = new CapturingSink();
            return new ClientHarness(factory.CreateDirectClient(options, sink, provider), sink, null, null);
        }

        var logger = defaultLogger ? new CapturingLoggerProvider() : null;
        var factorySink = defaultLogger ? null : new CapturingSink();
        var client = factory.CreateFactoryClient(
            configure,
            services =>
            {
                if (provider is not null)
                {
                    services.AddSingleton<IJsonBodyMaskerProvider>(provider);
                }

                if (factorySink is not null)
                {
                    services.AddSingleton<IJsonBodyLogSink>(factorySink);
                }

                if (logger is not null)
                {
                    services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(logger);
                }
            });
        return new ClientHarness(client.Client, factorySink, logger, client);
    }

    internal sealed class ClientHarness(
        HttpClient client,
        CapturingSink? sink,
        CapturingLoggerProvider? logger,
        FactoryClient? owner) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public CapturingSink? Sink { get; } = sink;

        public CapturingLoggerProvider? Logger { get; } = logger;

        public void Dispose()
        {
            if (owner is null)
            {
                Client.Dispose();
            }
            else
            {
                owner.Dispose();
            }
        }
    }

    private sealed class StaticMaskerProvider(JsonObserver masker) : IJsonBodyMaskerProvider
    {
        public JsonObserver? GetMasker(Type? modelType, string clientName) => masker;
    }
}
