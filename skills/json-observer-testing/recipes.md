# Recipes — json-observer-testing

Each block is a complete xUnit v3 test file that compiles and passes; `LogMaskers` and the record types stand in for your production code.

## A reusable leak fuzz helper

Put the fuzz loop in one helper and call it for every masker and payload that matters. It cuts the payload at every byte and, for an allow-list observer, also corrupts random bytes with a fixed seed. It collects every failure before asserting, so one run shows them all.

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
    public static readonly JsonObserver BlockListed = JsonObserver.Obj(AnyDepth(rules => rules.Match("password").Mask("***"), BlockList));

    public static readonly JsonObserver AllowListed = JsonObserver.Obj(root => root.Match("user").Unmasked());
}

public static class MaskingFuzz
{
    /// <summary>
    /// Masks every prefix of <paramref name="payload"/> (and, when <paramref name="corruptions"/> is positive, that many randomly
    /// corrupted copies) and fails when a call throws, writes a secret, or writes output that is not valid JSON.
    /// Corrupt only with an allow-list observer: a corrupted property name legitimately escapes a block-list rule.
    /// </summary>
    public static void AssertNoLeak(JsonObserver observer, string payload, IReadOnlyList<string> secrets, int corruptions = 0, int seed = 1)
    {
        var utf8 = Encoding.UTF8.GetBytes(payload);
        var failures = new List<string>();
        var output = new ArrayBufferWriter<byte>();

        for (var cut = 0; cut <= utf8.Length; cut++)
        {
            Check(utf8.AsSpan(0, cut), $"prefix {cut}");
        }

        var random = new Random(seed);
        for (var i = 0; i < corruptions; i++)
        {
            var corrupted = utf8.ToArray();
            for (var flips = random.Next(1, 4); flips > 0; flips--)
            {
                corrupted[random.Next(corrupted.Length)] = (byte)random.Next(256);
            }

            Check(corrupted.AsSpan(0, random.Next(corrupted.Length + 1)), $"corruption {i}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));

        void Check(ReadOnlySpan<byte> input, string label)
        {
            output.ResetWrittenCount();
            MaskResult result;
            try
            {
                result = observer.Mask(input, output);
            }
            catch (Exception ex)
            {
                failures.Add($"{label}: threw {ex.GetType().Name}");
                return;
            }

            var text = Encoding.UTF8.GetString(output.WrittenSpan);
            foreach (var secret in secrets.Where(s => text.Contains(s, StringComparison.Ordinal)))
            {
                failures.Add($"{label}: {result.Status} leaked '{secret}' in {text}");
            }

            if (result.BytesWritten > 0 && !IsJson(text))
            {
                failures.Add($"{label}: {result.Status} wrote invalid JSON {text}");
            }
        }
    }

    private static bool IsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed class LogMaskersFuzzTests
{
    private const string Payload = """{"user":"bob","password":"S3cr3t-7f2a","profile":{"password":"S3cr3t-7f2a","age":41},"list":[{"password":"S3cr3t-7f2a"}]}""";

    [Fact]
    public void BlockListed_Prefixes_NeverLeak() =>
        MaskingFuzz.AssertNoLeak(LogMaskers.BlockListed, Payload, ["S3cr3t-7f2a"]);

    [Fact]
    public void AllowListed_PrefixesAndCorruptions_NeverLeak() =>
        MaskingFuzz.AssertNoLeak(LogMaskers.AllowListed, Payload, ["S3cr3t-7f2a", "41"], corruptions: 2_000, seed: 20261003);
}
```

## A regression test for a reported leak

1. **Take the payload from the report** and replace real data with distinctive fakes, keeping the structure exactly — the structure is usually what defeated the rule.
2. **Write the test against the current masker and watch it fail.** A regression test that passes before the fix does not reproduce the leak.
3. Fix the rule; the test goes green. Keep the failing shape in the test name.

Below, a leak through an array: `Match("credentials", "secret")` does not cross the array, because an array item is a path level of its own. The fixed rule names the item level.

```csharp
using DragoAnt.System.Text.Json.Observer;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public static class LogMaskers
{
    private static readonly NameMatch AnyItem = new(_ => true);

    public static readonly JsonObserver BeforeFix = JsonObserver.Obj(AnyDepth(rules => rules
            .Path("credentials", "secret").Mask("***"),
        BlockList));

    public static readonly JsonObserver Body = JsonObserver.Obj(AnyDepth(rules => rules
            .Path("credentials", "secret").Mask("***")
            .Path("credentials", AnyItem, "secret").Mask("***"),
        BlockList));
}

public sealed class CredentialsLeakRegressionTests
{
    private const string ReportedShape = """{"account":"a-1","credentials":[{"kind":"api","secret":"fake-secret-91c3"}]}""";

    [Fact]
    public void SecretInsideCredentialsArray_IsMasked() =>
        Assert.DoesNotContain("fake-secret-91c3", LogMaskers.Body.Mask(ReportedShape));

    [Fact]
    public void SecretInsideCredentialsArray_LeakedBeforeTheFix() =>
        Assert.Contains("fake-secret-91c3", LogMaskers.BeforeFix.Mask(ReportedShape));

    [Fact]
    public void SecretInsideCredentialsObject_StillMasked() =>
        Assert.DoesNotContain("fake-secret-91c3", LogMaskers.Body.Mask("""{"credentials":{"secret":"fake-secret-91c3"}}"""));
}
```

## Test every model a masker provider serves

Serialize a sample of each model with secret-looking values in its sensitive properties — the same serializer settings the `HttpClient` uses — and mask it with the observer the provider returns. Adding a model to the provider then needs one line in the test data, and forgetting its masker fails the test.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;
using DragoAnt.Observer;
using Xunit;

public sealed record SignUp(string Email, string Password, string Country);

public sealed record Payment(string CardNumber, decimal Amount);

public sealed class ApiMaskers : IJsonBodyMaskerProvider
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    private static readonly Dictionary<Type, JsonObserver> Maskers = new()
    {
        [typeof(SignUp)] = For<SignUp>(),
        [typeof(Payment)] = For<Payment>(),
    };

    public JsonObserver? GetMasker(Type? modelType, string clientName) =>
        modelType is not null && Maskers.TryGetValue(modelType, out var masker) ? masker : null;

    private static JsonObserver For<T>() => JsonObserver.FromShape(JsonShape.FromTypeInfo(
        Json.GetTypeInfo(typeof(T)),
        property => property.Name switch
        {
            "email" => MaskTag.Hash,
            "password" => MaskTag.Full,
            "cardNumber" => MaskTag.Last4,
            _ => null,
        }));
}

public sealed class ApiMaskersTests
{
    private static readonly (object Sample, string[] Secrets)[] Samples =
    [
        (new SignUp("alice@example.com", "S3cr3t-7f2a", "NL"), ["alice@example.com", "S3cr3t-7f2a"]),
        (new Payment("4111111111111111", 10.5m), ["4111111111111111"]),
    ];

    [Theory]
    [InlineData(typeof(SignUp))]
    [InlineData(typeof(Payment))]
    public void EveryModel_HasAMasker(Type model) =>
        Assert.NotNull(new ApiMaskers().GetMasker(model, "api"));

    [Fact]
    public void EverySample_IsMaskedWithoutLosingSafeFields()
    {
        foreach (var (sample, secrets) in Samples)
        {
            var masker = new ApiMaskers().GetMasker(sample.GetType(), "api")!;
            var masked = masker.Mask(JsonSerializer.Serialize(sample, sample.GetType(), ApiMaskers.Json))!;

            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, masked);
            }
        }

        var signUp = new ApiMaskers().GetMasker(typeof(SignUp), "api")!.Mask("""{"email":"a@b.c","password":"p","country":"NL"}""");
        Assert.Contains("\"country\":\"NL\"", signUp);
    }

    [Fact]
    public void AFieldTheModelDoesNotKnow_IsMasked() =>
        Assert.Equal(
            """{"cardNumber":"***1111","amount":10.5,"cvv":"***"}""",
            new ApiMaskers().GetMasker(typeof(Payment), "api")!.Mask("""{"cardNumber":"4111111111111111","amount":10.5,"cvv":"123"}"""));
}
```
