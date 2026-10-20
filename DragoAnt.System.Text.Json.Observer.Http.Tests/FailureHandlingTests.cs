using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.Tests;

public sealed class FailureHandlingTests
{
    [Fact]
    public async Task HttpClientTimeout_LoggedAsCanceled()
    {
        var sink = new CapturingSink();
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.OnFailure },
            sink,
            async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/slow")
        {
            Content = HttpTestDoubles.JsonContent("""{"secret":"pw"}"""),
        };

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.SendAsync(request));

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(JsonBodyOutcome.Canceled, entry.Outcome);
        Assert.Null(entry.StatusCode);
        Assert.IsAssignableFrom<OperationCanceledException>(entry.Exception);
        Assert.DoesNotContain("pw", entry.RequestBody);
    }

    [Fact]
    public async Task CallerCancel_LoggedAsCanceledAndRethrown()
    {
        var sink = new CapturingSink();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.OnFailure },
            sink,
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://example.test/cancel", cts.Token));

        Assert.Equal(JsonBodyOutcome.Canceled, Assert.Single(sink.Entries).Outcome);
    }

    [Fact]
    public async Task Never_Canceled_NotLogged()
    {
        var sink = new CapturingSink();
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Never },
            sink,
            (_, _) => Task.FromException<HttpResponseMessage>(new OperationCanceledException()));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://example.test/never"));

        Assert.Empty(sink.Entries);
    }

    [Fact]
    public async Task Exception_RethrownWithOriginalStackTrace()
    {
        var sink = new CapturingSink();
        var original = new HttpRequestException("network failure");
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.OnFailure },
            sink,
            (_, _) => ThrowingInnerSend(original));
        using var client = new HttpClient(handler);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.test/fail"));

        Assert.Same(original, thrown);
        Assert.Contains(nameof(ThrowingInnerSend), thrown.StackTrace);
        var entry = Assert.Single(sink.Entries);
        Assert.Equal(JsonBodyOutcome.Exception, entry.Outcome);
        Assert.Same(original, entry.Exception);
    }

    [Fact]
    public async Task CallerCancelAfterSuccess_ReturnsResponse()
    {
        var sink = new CapturingSink();
        using var cts = new CancellationTokenSource();
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ =>
            {
                var response = HttpTestDoubles.Json(HttpStatusCode.OK, """{"a":1}""");
                cts.Cancel();
                return response;
            });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://example.test/ok", HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal("""{"a":1}""", await response.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal(JsonBodyOutcome.Success, (await sink.WaitSingleAsync()).Outcome);
    }

    [Fact]
    public async Task SinkThrows_ResponseStillReturned_WarningLoggedOnce()
    {
        var sink = new CapturingSink { Throw = _ => true };
        var logger = new CapturingLogger<JsonBodyLoggingHandler>();
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => HttpTestDoubles.Json(HttpStatusCode.OK, """{"a":1}"""),
            logger: logger);
        using var client = new HttpClient(handler);

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync("https://example.test/ok");
            Assert.Equal("""{"a":1}""", await response.Content.ReadAsStringAsync());
        }

        var failures = logger.Records.Where(r => r.Exception is InvalidOperationException).ToArray();
        Assert.Equal(3, failures.Length);
        Assert.Single(failures, r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SinkThrows_OnException_OriginalExceptionRethrown()
    {
        var sink = new CapturingSink { Throw = _ => true };
        var original = new HttpRequestException("network failure");
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.OnFailure },
            sink,
            (_, _) => Task.FromException<HttpResponseMessage>(original));
        using var client = new HttpClient(handler);

        Assert.Same(original, await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.test/fail")));
    }

    [Fact]
    public async Task LoggerThrows_ResponseStillReturned()
    {
        var sink = new CapturingSink { Throw = _ => true };
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => HttpTestDoubles.Json(HttpStatusCode.OK, """{"a":1}"""),
            logger: new ThrowingLogger());
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://example.test/ok");

        Assert.Equal("""{"a":1}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProviderThrows_ResponseIntact_BodyMarkedFailed()
    {
        var sink = new CapturingSink();
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => HttpTestDoubles.Json(HttpStatusCode.OK, """{"secret":"pw"}"""),
            new FuncMaskerProvider((_, _) => throw new InvalidOperationException("provider failure")));
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://example.test/ok");

        Assert.Equal("""{"secret":"pw"}""", await response.Content.ReadAsStringAsync());
        var entry = await sink.WaitSingleAsync();
        Assert.Equal(JsonBodyStatus.Failed, entry.ResponseBodyStatus);
        Assert.Equal("[body not logged: masking failed]", entry.ResponseBody);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<HttpResponseMessage> ThrowingInnerSend(Exception exception)
    {
        await Task.Yield();
        throw exception;
    }

    private sealed class ThrowingLogger : ILogger<JsonBodyLoggingHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("logger failure");
    }
}
