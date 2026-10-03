using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.Tests;

public sealed class ResponseCaptureTests
{
    [Fact]
    public async Task ConcurrentResponses_EachCallerReadsOwnBody()
    {
        const int iterations = 20;
        const int parallel = 64;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var sink = new CapturingSink();
            var handler = HttpTestDoubles.Handler(
                new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
                sink,
                request => HttpTestDoubles.Json(HttpStatusCode.OK, BodyFor(request.RequestUri!.AbsolutePath)));
            using var client = new HttpClient(handler);

            var mismatches = await Task.WhenAll(Enumerable.Range(0, parallel).Select(async i =>
            {
                var path = $"/item/{iteration}/{i}";
                using var response = await client.GetAsync($"https://example.test{path}", HttpCompletionOption.ResponseHeadersRead);
                await Task.Delay(10);
                var body = await response.Content.ReadAsStringAsync();
                return body == BodyFor(path) ? 0 : 1;
            }));

            Assert.Equal(0, mismatches.Sum());
        }

        static string BodyFor(string path)
        {
            var filler = new string((char)('a' + (path.GetHashCode() & 15)), 2000);
            return $$"""{"path":"{{path}}","data":"{{filler}}"}""";
        }
    }

    [Fact]
    public async Task SlowStreamingResponse_ReturnsHeadersImmediately()
    {
        var sink = new CapturingSink();
        var stream = new ScriptedStream([Encoding.UTF8.GetBytes("""{"event":1""")], ScriptedStream.Tail.Hang);
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => StreamResponse(HttpStatusCode.OK, stream));
        using var client = new HttpClient(handler);

        var send = client.GetAsync("https://example.test/events", HttpCompletionOption.ResponseHeadersRead);
        var winner = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(send, winner);
        using var response = await send;
        var body = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[64];
        var read = await body.ReadAsync(buffer);
        Assert.Equal("""{"event":1""", Encoding.UTF8.GetString(buffer, 0, read));
    }

    [Fact]
    public async Task SlowStreamingResponse_DisposedBeforeEnd_LogsIncompleteAndDisposesSource()
    {
        var sink = new CapturingSink();
        var stream = new ScriptedStream([Encoding.UTF8.GetBytes("""{"event":"secret-value","n":1""")], ScriptedStream.Tail.Hang);
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => StreamResponse(HttpStatusCode.OK, stream));
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://example.test/events", HttpCompletionOption.ResponseHeadersRead);
        response.Dispose();

        var entry = await sink.WaitSingleAsync();
        Assert.Equal(JsonBodyStatus.Incomplete, entry.ResponseBodyStatus);
        Assert.DoesNotContain("secret-value", entry.ResponseBody);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task ResponseRead_HonoursCallerCancellation()
    {
        var sink = new CapturingSink();
        var stream = new ScriptedStream([Encoding.UTF8.GetBytes("""{"a":""")], ScriptedStream.Tail.Hang);
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => StreamResponse(HttpStatusCode.OK, stream));
        using var client = new HttpClient(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://example.test/slow", cts.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        var entry = await sink.WaitSingleAsync();
        Assert.Equal(JsonBodyStatus.Incomplete, entry.ResponseBodyStatus);
    }

    [Fact]
    public async Task PartialPrefixReadFailure_CallerGetsPrefixThenError()
    {
        var sink = new CapturingSink();
        var first = Encoding.UTF8.GetBytes("{\"a\":\"" + new string('x', 1024));
        var stream = new ScriptedStream([first], ScriptedStream.Tail.Fail);
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ => StreamResponse(HttpStatusCode.OK, stream));
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://example.test/reset", HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStreamAsync();
        var received = new MemoryStream();
        var buffer = new byte[100];
        var failure = await Assert.ThrowsAsync<IOException>(async () =>
        {
            int read;
            while ((read = await body.ReadAsync(buffer)) > 0)
            {
                received.Write(buffer, 0, read);
            }
        });

        Assert.Equal("connection reset", failure.Message);
        Assert.Equal(first, received.ToArray());
        var entry = await sink.WaitSingleAsync();
        Assert.Equal(JsonBodyStatus.Incomplete, entry.ResponseBodyStatus);
    }

    [Fact]
    public async Task LargeResponse_CallerReadsFullBody_LoggedTruncatedAndParseable()
    {
        var sink = new CapturingSink();
        var json = $$"""{"content":"{{new string('x', 10_000)}}","tail":1}""";
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always, MaxBodyBytes = 100 },
            sink,
            _ => HttpTestDoubles.Json(HttpStatusCode.OK, json));
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://example.test/large");

        Assert.Equal(json, await response.Content.ReadAsStringAsync());
        var entry = await sink.WaitSingleAsync();
        Assert.True(entry.Truncated);
        Assert.Equal(JsonBodyStatus.Truncated, entry.ResponseBodyStatus);
        using var _ = global::System.Text.Json.JsonDocument.Parse(entry.ResponseBody!);
    }

    [Fact]
    public async Task ResponseContentHeaders_PreservedAfterCapture()
    {
        var sink = new CapturingSink();
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always },
            sink,
            _ =>
            {
                var response = HttpTestDoubles.Json(HttpStatusCode.OK, """{"a":1}""");
                response.Content.Headers.ContentLanguage.Add("en");
                return response;
            });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://example.test/headers");

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("en", response.Content.Headers.ContentLanguage);
    }

    [Fact]
    public async Task OnFailure_Success_ResponseContentNotWrapped()
    {
        var sink = new CapturingSink();
        HttpContent? original = null;
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.OnFailure },
            sink,
            _ =>
            {
                var response = HttpTestDoubles.Json(HttpStatusCode.OK, """{"a":1}""");
                original = response.Content;
                return response;
            });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://example.test/ok");

        Assert.Same(original, response.Content);
        Assert.Empty(sink.Entries);
    }

    private static HttpResponseMessage StreamResponse(HttpStatusCode status, Stream stream)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(status) { Content = content };
    }
}
