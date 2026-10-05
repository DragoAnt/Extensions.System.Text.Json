# Examples — json-observer-testing

Each block is a complete xUnit v3 test file: a test project referencing `xunit.v3` and `DragoAnt.System.Text.Json.Observer` (plus `.Http` for the last one). The `LogMaskers` class stands in for **your production code** — in a real project the tests reference it instead of declaring it.

## Golden cases

Exact input → output pairs. Raw string literals keep the JSON readable; one `[InlineData]` per behaviour you rely on.

```csharp
using DragoAnt.System.Text.Json.Observer;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public static class LogMaskers
{
    public static readonly JsonObserver Body = JsonObserver.Obj(AnyDepth(rules => rules
            .Match(Names.OneOf("password", "pin")).Mask(MaskTag.Full)
            .Path("card", "number").Mask(MaskTag.Last4),
        BlockList));
}

public sealed class LogMaskersGoldenTests
{
    [Theory]
    [InlineData("""{"user":"bob","password":"p"}""", """{"user":"bob","password":"***"}""")]
    [InlineData("""{"PIN":1234}""", """{"PIN":"***"}""")]
    [InlineData("""{"card":{"number":"4111111111111111","brand":"visa"}}""", """{"card":{"number":"***1111","brand":"visa"}}""")]
    [InlineData("""{"a":{"b":{"password":{"old":"p1","new":"p2"}}}}""", """{"a":{"b":{"password":"***"}}}""")]
    [InlineData("""{"password":null}""", """{"password":null}""")]
    public void Body_MasksSensitiveFields(string input, string expected) =>
        Assert.Equal(expected, LogMaskers.Body.Mask(input));
}
```

## The secret never appears

One secret value, many shapes that could defeat a rule. Assert absence first, then that the non-sensitive data survived — so a masker that masks everything fails too.

```csharp
using DragoAnt.System.Text.Json.Observer;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public static class LogMaskers
{
    public static readonly JsonObserver Body = JsonObserver.Any(_ => { }, _ => { }, AnyDepth(rules => rules
            .Match(Names.Contains("password")).Mask("***")
            .Match(Names.EndsWith("token")).Mask("***"),
        BlockList));
}

public sealed class LogMaskersSecretTests
{
    private const string Secret = "S3cr3t-7f2a";

    [Theory]
    [InlineData("""{"password":"S3cr3t-7f2a","keep":"visible"}""")]
    [InlineData("""{"login":{"newPassword":"S3cr3t-7f2a"},"keep":"visible"}""")]
    [InlineData("""{"users":[{"PASSWORD":"S3cr3t-7f2a"}],"keep":"visible"}""")]
    [InlineData("""[{"refresh_token":"S3cr3t-7f2a"},{"keep":"visible"}]""")]
    [InlineData("""{"password":{"value":"S3cr3t-7f2a","hint":"S3cr3t-7f2a"},"keep":"visible"}""")]
    [InlineData("""{"password":["S3cr3t-7f2a"],"keep":"visible"}""")]
    public void Body_NeverContainsTheSecret(string input)
    {
        var output = LogMaskers.Body.Mask(input, out var result);

        Assert.Equal(MaskStatus.Masked, result.Status);
        Assert.DoesNotContain(Secret, output);
        Assert.Contains("visible", output);
    }

    [Fact]
    public void Body_NumericSecret_IsMasked()
    {
        var output = LogMaskers.Body.Mask("""{"accessToken":987654321}""");

        Assert.DoesNotContain("987654321", output);
    }
}
```

## The rule is load-bearing

A secret-absent test is only worth something if it fails without the rule. Keep that proof as a test: build the same masker without the rule and show the secret comes through, using the **same** payload. When the production masker is built by a method with a switch for the rule, this costs two lines.

```csharp
using DragoAnt.System.Text.Json.Observer;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public static class LogMaskers
{
    public static readonly JsonObserver Body = Build(maskCvv: true);

    internal static JsonObserver Build(bool maskCvv) => JsonObserver.Obj(AnyDepth(rules =>
    {
        rules.Match("password").Mask("***");
        if (maskCvv)
        {
            rules.Match("cvv").Mask("***");
        }
    }, BlockList));
}

public sealed class LogMaskersLoadBearingTests
{
    private const string Payload = """{"card":{"cvv":731,"brand":"visa"}}""";

    [Fact]
    public void Cvv_IsMasked() => Assert.DoesNotContain("731", LogMaskers.Body.Mask(Payload));

    [Fact]
    public void Cvv_WithoutTheRule_Leaks_SoTheTestAboveCanFail() =>
        Assert.Contains("731", LogMaskers.Build(maskCvv: false).Mask(Payload));
}
```

## Truncation fuzz

Logged bodies are often cut at a size limit. Feed every prefix of a realistic payload through the UTF-8 API: it must never throw, never write a secret, and produce valid JSON whenever it writes anything.

```csharp
using System.Buffers;
using System.Text;
using System.Text.Json;
using DragoAnt.System.Text.Json.Observer;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public static class LogMaskers
{
    public static readonly JsonObserver Body = JsonObserver.Obj(AnyDepth(rules => rules
            .Match("password").Mask("***")
            .Match("pin").Mask("***"),
        BlockList));
}

public sealed class LogMaskersTruncationTests
{
    private const string Secret = "S3cr3t-7f2a";

    [Fact]
    public void EveryPrefix_IsValidJson_WithoutTheSecret()
    {
        var payload = Encoding.UTF8.GetBytes(
            $$"""{"user":"bob","password":"{{Secret}}","items":[{"pin":"{{Secret}}","qty":1.5},{"pin":["{{Secret}}"]}],"ok":true}""");
        var output = new ArrayBufferWriter<byte>();

        for (var cut = 0; cut <= payload.Length; cut++)
        {
            output.ResetWrittenCount();
            var result = LogMaskers.Body.Mask(payload.AsSpan(0, cut), output);
            var text = Encoding.UTF8.GetString(output.WrittenSpan);

            Assert.DoesNotContain(Secret, text);
            Assert.Equal(result.BytesWritten, output.WrittenCount);
            if (result.BytesWritten > 0)
            {
                using var parsed = JsonDocument.Parse(text);
            }

            var expected = cut == payload.Length ? MaskStatus.Masked : cut == 0 ? MaskStatus.Unrecognized : MaskStatus.Truncated;
            Assert.Equal(expected, result.Status);
        }
    }
}
```

## Corruption fuzz

Random byte damage with a **fixed seed** (reproducible failures), on an **allow-list** observer: a corrupted property name is then still masked, so "no secret" must hold for every case. Any status is acceptable; throwing, leaking, or unparseable output is not.

```csharp
using System.Buffers;
using System.Text;
using System.Text.Json;
using DragoAnt.System.Text.Json.Observer;
using Xunit;

public static class LogMaskers
{
    public static readonly JsonObserver AllowListed = JsonObserver.Any(
        root => root.Match("user").Unmasked().Match("ok").Unmasked(),
        _ => { });
}

public sealed class LogMaskersCorruptionTests
{
    private const string Secret = "S3cr3t-7f2a";

    [Fact]
    public void CorruptedPayloads_NeverThrowNeverLeak()
    {
        var payload = Encoding.UTF8.GetBytes($$"""{"user":"bob","password":"{{Secret}}","items":[{"pin":"{{Secret}}"}],"ok":true}""");
        var random = new Random(20261003);
        var output = new ArrayBufferWriter<byte>();

        for (var i = 0; i < 2_000; i++)
        {
            var corrupted = payload.ToArray();
            for (var flips = random.Next(1, 4); flips > 0; flips--)
            {
                corrupted[random.Next(corrupted.Length)] = (byte)random.Next(256);
            }

            output.ResetWrittenCount();
            var result = LogMaskers.AllowListed.Mask(corrupted.AsSpan(0, random.Next(corrupted.Length + 1)), output);
            var text = Encoding.UTF8.GetString(output.WrittenSpan);

            Assert.DoesNotContain(Secret, text);
            if (result.BytesWritten > 0)
            {
                using var parsed = JsonDocument.Parse(text);
            }
        }
    }
}
```

## Allocation budget

Warm up first (path buffers grow on the first calls), reuse the output buffer, measure many calls on the current thread, and compare the average with a budget taken from a measurement plus headroom. Keep the payload and rule kinds representative: a masking function allocates the `string` it receives, so it needs a larger budget than constant or tag rules.

```csharp
using System.Buffers;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public static class LogMaskers
{
    public static readonly JsonObserver Body = JsonObserver.Obj(AnyDepth(rules => rules.Match("password").Mask("***"), BlockList));
}

public sealed class LogMaskersAllocationTests
{
    [Theory]
    [InlineData(1_024, 1_024)]
    [InlineData(64 * 1_024, 1_024)]
    public void Utf8Api_StaysWithinBudget(int size, long budgetPerCall)
    {
        var payload = Payload(size);
        var output = new ArrayBufferWriter<byte>(payload.Length * 2);
        for (var i = 0; i < 20; i++)
        {
            output.ResetWrittenCount();
            LogMaskers.Body.Mask(payload, output);
        }

        const int calls = 50;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < calls; i++)
        {
            output.ResetWrittenCount();
            LogMaskers.Body.Mask(payload, output);
        }

        var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / calls;
        Assert.True(perCall <= budgetPerCall, $"{size} B payload allocates {perCall} B per call");
    }

    private static byte[] Payload(int size)
    {
        var json = new StringBuilder("""{"password":"secret" """);
        for (var i = 0; json.Length < size; i++)
        {
            json.Append(",\"field").Append(i).Append("\":\"value ").Append(i).Append('"');
        }

        return Encoding.UTF8.GetBytes(json.Append('}').ToString());
    }
}
```

## Concurrency

An observer is meant to be shared. Run the same inputs on many threads at once — including the very first calls, while internal buffers are still sized — and compare with the single-threaded result.

```csharp
using DragoAnt.System.Text.Json.Observer;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public sealed class SharedObserverTests
{
    [Fact]
    public void ConcurrentCalls_GiveTheSameOutput()
    {
        var observer = JsonObserver.Obj(AnyDepth(rules => rules.Match("password").Mask("***"), BlockList));
        var deep = """{"a":{"b":{"c":{"d":{"e":{"f":{"g":{"password":"p","keep":1}}}}}}}}""";
        var flat = """{"password":"p","keep":1}""";
        var expectedDeep = """{"a":{"b":{"c":{"d":{"e":{"f":{"g":{"password":"***","keep":1}}}}}}}}""";
        var expectedFlat = """{"password":"***","keep":1}""";

        var failures = 0;
        Parallel.For(0, 2_000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var (input, expected) = i % 2 == 0 ? (deep, expectedDeep) : (flat, expectedFlat);
            if (observer.Mask(input) != expected)
            {
                Interlocked.Increment(ref failures);
            }
        });

        Assert.Equal(0, failures);
    }
}
```

## HTTP body logging

Test `JsonBodyLoggingHandler` without a network: a fake inner handler returns a canned response, a capturing sink records entries, and a provider returns your production observers. The response entry is written on a background read — wait for it with a timeout. Assert three things: the secrets are absent from the entry, the non-sensitive data is present, and the caller still received the full, unmasked response.

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public sealed record Login(string User, string Password);

public sealed record Session(string Token, string User);

public sealed class AuthMaskers : IJsonBodyMaskerProvider
{
    private static readonly JsonObserver Auth = JsonObserver.Obj(AnyDepth(rules => rules
            .Match("password").Mask("***")
            .Match("token").Mask("***"),
        BlockList));

    public JsonObserver? GetMasker(Type? modelType, string clientName) =>
        modelType == typeof(Login) || modelType == typeof(Session) ? Auth : null;
}

public sealed class AuthClientLoggingTests
{
    [Fact]
    public async Task FailedLogin_LogsMaskedBodies_AndCallerGetsFullResponse()
    {
        var sink = new CapturingSink();
        var inner = new StubHandler(HttpStatusCode.Unauthorized, """{"token":"tok-9f1c","user":"bob","error":"locked"}""");
        var handler = new JsonBodyLoggingHandler(new JsonBodyLoggingOptions(), new AuthMaskers(), sink) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://auth.example.com/login?otp=123456")
        {
            Content = new StringContent("""{"user":"bob","password":"pw-5e8d"}""", Encoding.UTF8, "application/json"),
        }.WithBodyLogging<Login, Session>("Login");
        using var response = await client.SendAsync(request);
        var received = await response.Content.ReadAsStringAsync();

        var entry = await sink.WaitAsync();
        Assert.Equal(JsonBodyOutcome.Failure, entry.Outcome);
        Assert.Equal("/login", entry.Path);
        Assert.Equal(JsonBodyStatus.Masked, entry.RequestBodyStatus);
        Assert.Equal(JsonBodyStatus.Masked, entry.ResponseBodyStatus);
        Assert.DoesNotContain("pw-5e8d", entry.RequestBody);
        Assert.DoesNotContain("tok-9f1c", entry.ResponseBody);
        Assert.Contains("locked", entry.ResponseBody);
        Assert.Contains("tok-9f1c", received);
    }

    [Fact]
    public async Task SuccessfulCall_IsNotLogged_ByDefault()
    {
        var sink = new CapturingSink();
        var handler = new JsonBodyLoggingHandler(new JsonBodyLoggingOptions(), new AuthMaskers(), sink)
        {
            InnerHandler = new StubHandler(HttpStatusCode.OK, """{"token":"t","user":"bob"}"""),
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://auth.example.com/me");
        await response.Content.ReadAsStringAsync();

        Assert.Empty(sink.Entries);
    }
}

public sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
}

public sealed class CapturingSink : IJsonBodyLogSink
{
    private readonly ConcurrentQueue<JsonBodyLogEntry> _entries = new();
    private readonly SemaphoreSlim _written = new(0);

    public IReadOnlyCollection<JsonBodyLogEntry> Entries => _entries.ToArray();

    public void Write(in JsonBodyLogEntry entry)
    {
        _entries.Enqueue(entry);
        _written.Release();
    }

    public async Task<JsonBodyLogEntry> WaitAsync(TimeSpan? timeout = null)
    {
        if (!await _written.WaitAsync(timeout ?? TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("no log entry was written");
        }

        return Assert.Single(_entries);
    }
}
```

A call the options do not log (a success under `OnFailure`) never starts a capture, so "nothing was logged" can be asserted right after the call. A logged call's response entry, by contrast, always needs the wait.
