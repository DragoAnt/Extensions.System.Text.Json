using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.Tests;

public sealed class RegistrationAndSinkTests
{
    [Fact]
    public async Task AddJsonBodyLogging_Twice_LogsOnce()
    {
        var sink = new CapturingSink();
        var services = new ServiceCollection();
        services.AddSingleton<IJsonBodyLogSink>(sink);
        services.AddHttpClient("Api")
            .AddJsonBodyLogging(o => o.When = JsonBodyLogWhen.Always)
            .AddJsonBodyLogging(o => o.MaxBodyBytes = 10)
            .ConfigurePrimaryHttpMessageHandler(() => new FuncHandler((_, _) =>
                Task.FromResult(HttpTestDoubles.Json(HttpStatusCode.OK, """{"a":1}"""))));
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IHttpClientFactory>().CreateClient("Api").GetAsync("https://example.test/x");

        var entry = await sink.WaitSingleAsync();
        Assert.Equal("Api", entry.ClientName);
        Assert.Equal(10, provider.GetRequiredService<IOptionsMonitor<JsonBodyLoggingOptions>>().Get("Api").MaxBodyBytes);
    }

    [Fact]
    public async Task OptionsReload_AppliesNextCall()
    {
        var sink = new CapturingSink();
        var monitor = new MutableOptionsMonitor(new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Never });
        var handler = new JsonBodyLoggingHandler("Api", monitor, sink: sink)
        {
            InnerHandler = new FuncHandler((_, _) => Task.FromResult(HttpTestDoubles.Json(HttpStatusCode.OK, """{"a":1}"""))),
        };
        using var client = new HttpClient(handler);

        await client.GetAsync("https://example.test/first");
        monitor.Current = new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always };
        await client.GetAsync("https://example.test/second");

        Assert.Equal("/second", (await sink.WaitSingleAsync()).Path);
    }

    [Fact]
    public async Task RegisteredSinkAndProvider_ResolvedFromServices()
    {
        var sink = new CapturingSink();
        var services = new ServiceCollection();
        services.AddSingleton<IJsonBodyLogSink>(sink);
        services.AddSingleton<IJsonBodyMaskerProvider>(new FuncMaskerProvider((_, _) => null));
        services.AddHttpClient("Api")
            .AddJsonBodyLogging(o => o.When = JsonBodyLogWhen.Always)
            .ConfigurePrimaryHttpMessageHandler(() => new FuncHandler((_, _) =>
                Task.FromResult(HttpTestDoubles.Json(HttpStatusCode.OK, """{"a":1}"""))));
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IHttpClientFactory>().CreateClient("Api").GetAsync("https://example.test/x");

        Assert.Equal(JsonBodyStatus.Withheld, (await sink.WaitSingleAsync()).ResponseBodyStatus);
    }

    [Fact]
    public async Task OperationName_ReachesEntryAndLogMessage()
    {
        var sink = new CapturingSink();
        var logger = new CapturingLogger<JsonBodyLoggingHandler>();
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => HttpTestDoubles.Json(HttpStatusCode.OK, """{"a":1}"""));
        using var client = new HttpClient(handler);

        await client.SendWithBodyLoggingAsync<HttpTestRequest, HttpTestResponse>(
            new HttpRequestMessage(HttpMethod.Get, "https://example.test/x"),
            "CreateItem");

        var entry = await sink.WaitSingleAsync();
        Assert.Equal("CreateItem", entry.Operation);
        new LoggerJsonBodyLogSink(logger).Write(entry);
        Assert.Contains("CreateItem", Assert.Single(logger.Records).Message);
    }

    [Fact]
    public void LoggerSink_MessageCarriesOutcomeStatusAndFlags()
    {
        var logger = new CapturingLogger<JsonBodyLoggingHandler>();
        var entry = new JsonBodyLogEntry
        {
            ClientName = "Api",
            Method = HttpMethod.Post,
            Path = "/x",
            StatusCode = 500,
            Outcome = JsonBodyOutcome.Failure,
            RequestBody = """{"a":"***"}""",
            RequestBodyStatus = JsonBodyStatus.Masked,
            ResponseBody = "[invalid JSON]",
            ResponseBodyStatus = JsonBodyStatus.Invalid,
            Truncated = true,
            BodyUnmasked = false,
        };

        new LoggerJsonBodyLogSink(logger).Write(entry);

        var message = Assert.Single(logger.Records).Message;
        Assert.Contains("outcome=Failure", message);
        Assert.Contains("500", message);
        Assert.Contains("truncated=True", message);
        Assert.Contains("unmasked=False", message);
        Assert.Contains("response(Invalid)=[invalid JSON]", message);
    }

    [Theory]
    [InlineData(JsonBodyOutcome.Success, LogLevel.Information)]
    [InlineData(JsonBodyOutcome.Failure, LogLevel.Warning)]
    [InlineData(JsonBodyOutcome.Exception, LogLevel.Warning)]
    [InlineData(JsonBodyOutcome.Canceled, LogLevel.Warning)]
    public void LoggerSink_LevelFollowsOutcome(JsonBodyOutcome outcome, LogLevel expected)
    {
        var logger = new CapturingLogger<JsonBodyLoggingHandler>();

        new LoggerJsonBodyLogSink(logger).Write(new JsonBodyLogEntry { ClientName = "Api", Method = HttpMethod.Get, Path = "/", Outcome = outcome });

        Assert.Equal(expected, Assert.Single(logger.Records).Level);
    }

    private sealed class MutableOptionsMonitor(JsonBodyLoggingOptions current) : IOptionsMonitor<JsonBodyLoggingOptions>
    {
        public JsonBodyLoggingOptions Current { get; set; } = current;

        public JsonBodyLoggingOptions CurrentValue => Current;

        public JsonBodyLoggingOptions Get(string? name) => Current;

        public IDisposable? OnChange(Action<JsonBodyLoggingOptions, string?> listener) => null;
    }
}
