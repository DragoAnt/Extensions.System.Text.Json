using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace DragoAnt.System.Text.Json.Observer.Http.Tests;

public sealed class BodyRenderingTests
{
    private static readonly JsonBodyLoggingOptions Always = new() { When = JsonBodyLogWhen.Always };

    [Fact]
    public async Task NoProvider_RootArrayResponse_MaskedNotEmpty()
    {
        var entry = await LogResponseAsync(HttpTestDoubles.Json(HttpStatusCode.OK, """[{"a":"x"},{"a":"y"}]"""));

        Assert.Equal(JsonBodyStatus.Masked, entry.ResponseBodyStatus);
        Assert.Equal("""[{"a":"***"},{"a":"***"}]""", entry.ResponseBody);
    }

    [Fact]
    public async Task NoProvider_ObjectResponse_AllValuesMasked()
    {
        var entry = await LogResponseAsync(HttpTestDoubles.Json(HttpStatusCode.OK, """{"user":"alice","pin":1234}"""));

        Assert.DoesNotContain("alice", entry.ResponseBody);
        Assert.DoesNotContain("1234", entry.ResponseBody);
    }

    [Fact]
    public async Task InvalidJsonBody_LoggedWithMarker_NeverRaw()
    {
        var entry = await LogResponseAsync(HttpTestDoubles.Json(HttpStatusCode.InternalServerError, """{"a":"secret" "b":1}"""));

        Assert.Equal(JsonBodyStatus.Invalid, entry.ResponseBodyStatus);
        Assert.DoesNotContain("secret", entry.ResponseBody);
        Assert.False(string.IsNullOrEmpty(entry.ResponseBody));
    }

    [Fact]
    public async Task CutJsonBody_LoggedTruncated()
    {
        var entry = await LogResponseAsync(HttpTestDoubles.Json(HttpStatusCode.InternalServerError, """{"a":"""));

        Assert.Equal(JsonBodyStatus.Truncated, entry.ResponseBodyStatus);
        Assert.NotNull(entry.ResponseBody);
    }

    [Fact]
    public async Task MissingContentType_PlainText_NotLoggedAsEmpty()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new ByteArrayContent("internal error secret"u8.ToArray()) };

        var entry = await LogResponseAsync(response);

        Assert.Contains(entry.ResponseBodyStatus, new[] { JsonBodyStatus.Invalid, JsonBodyStatus.NotJson });
        Assert.StartsWith("[", entry.ResponseBody);
        Assert.DoesNotContain("secret", entry.ResponseBody);
    }

    [Fact]
    public async Task Charset16Body_NotMaskedAsUtf8()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"a":"b"}""", Encoding.Unicode, "application/json") };

        var entry = await LogResponseAsync(response);

        Assert.Equal(JsonBodyStatus.Skipped, entry.ResponseBodyStatus);
        Assert.Equal("[body not logged: charset utf-16]", entry.ResponseBody);
    }

    [Fact]
    public async Task GzipEncodedBody_Skipped()
    {
        var response = HttpTestDoubles.Json(HttpStatusCode.OK, """{"a":"b"}""");
        response.Content.Headers.ContentEncoding.Add("gzip");

        var entry = await LogResponseAsync(response);

        Assert.Equal("[body not logged: encoding gzip]", entry.ResponseBody);
    }

    [Fact]
    public async Task EmptyResponseBody_StatusNone()
    {
        var entry = await LogResponseAsync(new HttpResponseMessage(HttpStatusCode.NoContent));

        Assert.Equal(JsonBodyStatus.None, entry.ResponseBodyStatus);
        Assert.Null(entry.ResponseBody);
    }

    [Fact]
    public async Task QueryString_NotLogged()
    {
        var sink = new CapturingSink();
        var handler = HttpTestDoubles.Handler(Always, sink, _ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);

        await client.GetAsync("https://example.test/items/7?token=abc&api_key=def#frag");

        var entry = await sink.WaitSingleAsync();
        Assert.Equal("/items/7", entry.Path);
    }

    [Fact]
    public async Task IncludeSensitive_LogsRawAndFlagsUnmasked_WarnsOnce()
    {
        var sink = new CapturingSink();
        var logger = new CapturingLogger<JsonBodyLoggingHandler>();
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always, IncludeSensitive = true },
            sink,
            _ => HttpTestDoubles.Json(HttpStatusCode.OK, """{"secret":"pw"}"""),
            logger: logger);
        using var client = new HttpClient(handler);

        await client.GetAsync("https://example.test/a");
        await client.GetAsync("https://example.test/b");

        Assert.All(sink.Entries, entry =>
        {
            Assert.Equal("""{"secret":"pw"}""", entry.ResponseBody);
            Assert.Equal(JsonBodyStatus.Raw, entry.ResponseBodyStatus);
            Assert.True(entry.BodyUnmasked);
        });
        Assert.Single(logger.Records, r => r.Message.Contains("IncludeSensitive"));
    }

    [Fact]
    public async Task RequestBody_TruncatedAtMaxBodyBytes()
    {
        var json = $$"""{"data":"{{new string('x', 10_000)}}"}""";
        var entry = await LogRequestAsync(HttpTestDoubles.JsonContent(json), maxBodyBytes: 100);

        Assert.True(entry.Truncated);
        Assert.Equal(JsonBodyStatus.Truncated, entry.RequestBodyStatus);
        Assert.True(Encoding.UTF8.GetByteCount(entry.RequestBody!) <= 100);
    }

    [Fact]
    public async Task RequestBody_LargeContent_ReadsAtMostCap()
    {
        var content = new ChunkedJsonContent(totalBytes: 50 * 1024 * 1024, chunkBytes: 64 * 1024);

        var entry = await LogRequestAsync(content, maxBodyBytes: 100, sendBody: false);

        Assert.Equal(JsonBodyStatus.Truncated, entry.RequestBodyStatus);
        Assert.True(content.BytesWritten <= 64 * 1024, $"wrote {content.BytesWritten} bytes to log 100");
    }

    [Fact]
    public async Task SeekableStreamContent_LoggedAndStillReadable()
    {
        var stream = new MemoryStream("""{"secret":"pw","n":1}"""u8.ToArray());
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var entry = await LogRequestAsync(content);

        Assert.Equal(JsonBodyStatus.Masked, entry.RequestBodyStatus);
        Assert.DoesNotContain("pw", entry.RequestBody);
        Assert.Equal("""{"secret":"pw","n":1}""", await content.ReadAsStringAsync());
    }

    [Fact]
    public async Task JsonContent_RequestBodyLogged()
    {
        var entry = await LogRequestAsync(JsonContent.Create(new HttpTestRequest("alice", "pw")));

        Assert.Equal(JsonBodyStatus.Masked, entry.RequestBodyStatus);
        Assert.Contains("\"name\"", entry.RequestBody);
        Assert.DoesNotContain("pw", entry.RequestBody);
    }

    [Fact]
    public async Task ReadOnlyMemoryContent_RequestBodyLogged()
    {
        var content = new ReadOnlyMemoryContent("""{"a":"b"}"""u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var entry = await LogRequestAsync(content);

        Assert.Equal("""{"a":"***"}""", entry.RequestBody);
    }

    [Fact]
    public async Task MaxBodyBytesZero_BodiesSkipped()
    {
        var entry = await LogRequestAsync(HttpTestDoubles.JsonContent("""{"a":"b"}"""), maxBodyBytes: 0);

        Assert.Equal(JsonBodyStatus.Skipped, entry.RequestBodyStatus);
        Assert.Equal(JsonBodyStatus.Skipped, entry.ResponseBodyStatus);
        Assert.Equal("[body not logged: MaxBodyBytes is 0]", entry.RequestBody);
    }

    [Fact]
    public async Task MaxBodyBytesIntMax_DoesNotThrow()
    {
        var entry = await LogRequestAsync(HttpTestDoubles.JsonContent("""{"a":"b"}"""), maxBodyBytes: int.MaxValue);

        Assert.Equal("""{"a":"***"}""", entry.RequestBody);
        Assert.Equal("""{"ok":"***"}""", entry.ResponseBody);
    }

    [Fact]
    public async Task MaxBodyBytesNegative_TreatedAsZero()
    {
        var entry = await LogRequestAsync(HttpTestDoubles.JsonContent("""{"a":"b"}"""), maxBodyBytes: -5);

        Assert.Equal(JsonBodyStatus.Skipped, entry.RequestBodyStatus);
    }

    private static async Task<JsonBodyLogEntry> LogResponseAsync(HttpResponseMessage response)
    {
        var sink = new CapturingSink();
        var handler = HttpTestDoubles.Handler(Always, sink, _ => response);
        using var client = new HttpClient(handler);

        using var _ = await client.GetAsync("https://example.test/response");
        return await sink.WaitSingleAsync();
    }

    private static async Task<JsonBodyLogEntry> LogRequestAsync(HttpContent content, int maxBodyBytes = 4096, bool sendBody = true)
    {
        var sink = new CapturingSink();
        var handler = HttpTestDoubles.Handler(
            new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always, MaxBodyBytes = maxBodyBytes },
            sink,
            async (request, ct) =>
            {
                if (sendBody)
                {
                    await request.Content!.CopyToAsync(Stream.Null, ct);
                }

                return HttpTestDoubles.Json(HttpStatusCode.OK, """{"ok":true}""");
            });
        using var client = new HttpClient(handler);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/request") { Content = content };
        using var _ = await client.SendAsync(request);
        return await sink.WaitSingleAsync();
    }

    private sealed class ChunkedJsonContent : HttpContent
    {
        private readonly long _totalBytes;
        private readonly int _chunkBytes;

        public ChunkedJsonContent(long totalBytes, int chunkBytes)
        {
            _totalBytes = totalBytes;
            _chunkBytes = chunkBytes;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        public long BytesWritten { get; private set; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var chunk = new byte[_chunkBytes];
            chunk.AsSpan().Fill((byte)'1');
            chunk[0] = (byte)'[';
            for (long written = 0; written < _totalBytes; written += _chunkBytes)
            {
                BytesWritten += _chunkBytes;
                await stream.WriteAsync(chunk);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _totalBytes;
            return true;
        }
    }
}
