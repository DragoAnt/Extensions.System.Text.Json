using System.Buffers;
using System.Text;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class BytesApiTests
{
    private const string Secret = "S3cr3tV4l";

    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        Relative(b => b.Match("password").MaskAny("***").Match("pin").MaskAny("***"), BlockList));

    private static readonly string[] Payloads =
    [
        $$$"""{"user":"bob","password":"{{{Secret}}}","card":{"pin":"{{{Secret}}}","exp":"12/30"},"items":[{"id":1,"password":["{{{Secret}}}",{"x":"{{{Secret}}}"}]},{"id":2,"note":"ok"}],"active":true,"amount":1.5e3}""",
        $$$$$"""[{"password":{"value":"{{{{{Secret}}}}}","deep":[1,2,{"s":"{{{{{Secret}}}}}"}]}},null,"text",[{"pin":12345678}],{"a":{"b":{"c":{"password":"{{{{{Secret}}}}}"}}}}]""",
        $$$"""{"Ivan":"Иван <b>&","esc":"pa\"ss","password":"{{{Secret}}}","n":-0,"big":1e400,"list":[true,false,null,{"pin":"{{{Secret}}}"}]}""",
    ];

    internal static (MaskResult Result, string Output) Mask(JsonObserver observer, string json, JsonObserverOptions? options = null)
        => Mask(observer, Encoding.UTF8.GetBytes(json), options);

    internal static (MaskResult Result, string Output) Mask(JsonObserver observer, ReadOnlySpan<byte> utf8, JsonObserverOptions? options = null)
    {
        var output = new ArrayBufferWriter<byte>();
        var result = observer.Mask(utf8, output, options);
        output.WrittenCount.Should().Be(result.BytesWritten);
        return (result, Encoding.UTF8.GetString(output.WrittenSpan));
    }

    [Fact]
    public void Complete_ReturnsMasked()
    {
        var (result, output) = Mask(Observer, Payloads[0]);

        result.Status.Should().Be(MaskStatus.Masked);
        result.FailedAtByte.Should().Be(-1);
        output.Should().NotContain(Secret).And.Contain("\"amount\":1.5e3");
    }

    [Fact]
    public void Truncated_ReturnsSafePrefixAndTruncated()
    {
        var (result, output) = Mask(Observer, $$"""{"user":"bob","password":"{{Secret}}","a":{"b":[1,2""");

        result.Status.Should().Be(MaskStatus.Truncated);
        output.Should().Be("""{"user":"bob","password":"***","a":{"b":[1]}}""");
    }

    [Fact]
    public void Truncated_InsideSensitiveContainer_NeverWritesItsContent()
    {
        var (result, output) = Mask(Observer, $$"""{"password":{"v":"{{Secret}}","x":1,"y":""");

        result.Status.Should().Be(MaskStatus.Truncated);
        output.Should().Be("""{"password":"***"}""");
    }

    [Fact]
    public void UncompletedJson_MasksSensitiveAndSynthesizesClosingBraces()
    {
        var observer = JsonObserver.Any(
            _ => { },
            _ => { },
            Relative(b => b.Match("DriverLicense").MaskAny("***"), BlockList));

        var uncompletedJson = """
            {
               "user": {
                  "DriverLicense": "vvvvvvv3444"
            """;

        var (result, output) = Mask(observer, uncompletedJson);

        result.Status.Should().Be(MaskStatus.Truncated);
        output.Should().Be("""{"user":{"DriverLicense":"***"}}""");
        output.Should().NotContain("vvvvvvv3444");

        using var parsed = JsonDocument.Parse(output);
        parsed.RootElement.GetProperty("user").GetProperty("DriverLicense").GetString().Should().Be("***");
    }

    [Fact]
    public void UncompletedJson_IncompleteFieldName_RollsBackAndClosesContainers()
    {
        var observer = JsonObserver.Any(
            _ => { },
            _ => { },
            Relative(b => b.Match("DriverLicense").MaskAny("***"), BlockList));

        // Cut off mid-field name: "dr
        var uncompletedJson = """
            {
               "user": {
                  "dr
            """;

        var (result, output) = Mask(observer, uncompletedJson);

        result.Status.Should().Be(MaskStatus.Truncated);
        output.Should().Be("""{"user":{}}""");
        output.Should().NotContain("dr");

        using var parsed = JsonDocument.Parse(output);
        parsed.RootElement.GetProperty("user").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void UncompletedJson_IncompleteFieldValue_NeverLeaksAndClosesContainers()
    {
        var observer = JsonObserver.Any(
            _ => { },
            _ => { },
            Relative(b => b.Match("DriverLicense").MaskAny("***"), BlockList));

        // Cut off mid-field value: "vvv without closing quote
        var uncompletedJson = """
            {
               "user": {
                  "DriverLicense": "vvv
            """;

        var (result, output) = Mask(observer, uncompletedJson);

        result.Status.Should().Be(MaskStatus.Truncated);
        output.Should().Be("""{"user":{}}""");
        output.Should().NotContain("vvv");

        using var parsed = JsonDocument.Parse(output);
        parsed.RootElement.GetProperty("user").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void Invalid_ReturnsPrefixAndInvalid()
    {
        var json = $$"""{"user":"bob","password":"{{Secret}}","a": }""";

        var (result, output) = Mask(Observer, json);

        result.Status.Should().Be(MaskStatus.Invalid);
        result.FailedAtByte.Should().BeGreaterThan(0).And.BeLessThan(json.Length);
        output.Should().Be("""{"user":"bob","password":"***"}""");
    }

    [Theory]
    [InlineData("\"hello\"")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("null")]
    public void RootPrimitive_NotJson(string json)
    {
        var (result, output) = Mask(Observer, json);

        result.Status.Should().Be(MaskStatus.NotJson);
        output.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_NotJson(string json) => Mask(Observer, json).Result.Status.Should().Be(MaskStatus.NotJson);

    [Fact]
    public void CommentWithoutReaderOption_AutoAllowed()
    {
        var json = $$"""{/*c*/"password":"{{Secret}}"}""";

        var (result, output) = Mask(Observer, json);
        var masked = Observer.Mask(json);

        result.Status.Should().Be(MaskStatus.Masked);
        output.Should().Be("""{"password":"***"}""");
        masked.Should().Be("""{"password":"***"}""");
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "{}")]
    [InlineData(8, "{\"a\":{}}")]
    [InlineData(20, "{\"a\":{\"b\":[1,2,3]}}")]
    [InlineData(21, "{\"a\":{\"b\":[1,2,3,4]}}")]
    public void MaxOutputBytes_ClosesContainers(int max, string expected)
    {
        var (result, output) = Mask(Observer, """{"a":{"b":[1,2,3,4,5,6,7,8,9]},"c":1}""", new JsonObserverOptions(MaxOutputBytes: max));

        result.Status.Should().Be(MaskStatus.Truncated);
        output.Should().Be(expected);
        output.Length.Should().BeLessThanOrEqualTo(max);
    }

    [Fact]
    public void MaxValueBytes_TruncatesLongString()
    {
        var (result, output) = Mask(Observer, """{"note":"abcdefghij","ru":"ИванИван"}""", new JsonObserverOptions(MaxValueBytes: 5));

        result.Status.Should().Be(MaskStatus.Truncated);
        var root = JsonDocument.Parse(output).RootElement;
        root.GetProperty("note").GetString().Should().Be("abcde" + (char)0x2026);
        root.GetProperty("ru").GetString().Should().Be("Ив" + (char)0x2026);
    }

    [Fact]
    public void RelaxedEscaping_KeepsTextReadable()
    {
        Mask(Observer, """{"name":"Иван <b>&"}""").Output.Should().Be("""{"name":"Иван <b>&"}""");
        Mask(Observer, """{"name":"Иван"}""", new JsonObserverOptions(RelaxedEscaping: false)).Output.Should().NotContain("Иван");
    }

    [Fact]
    public void NeverThrows_Fuzz()
    {
        var random = new Random(20261001);
        var payloads = Payloads.Select(p => Encoding.UTF8.GetBytes(p)).ToArray();
        for (var i = 0; i < 10_000; i++)
        {
            var payload = payloads[i % payloads.Length];
            var cut = payload.AsSpan(0, random.Next(payload.Length + 1));

            var (result, output) = Mask(Observer, cut, new JsonObserverOptions(MaxOutputBytes: random.Next(2) == 0 ? int.MaxValue : random.Next(256)));

            output.Should().NotContain(Secret);
            if (result.BytesWritten > 0)
            {
                var parse = () => JsonDocument.Parse(output).Dispose();
                parse.Should().NotThrow(output);
            }

            var corrupted = payload.ToArray();
            corrupted[random.Next(corrupted.Length)] = (byte)random.Next(256);
            Mask(Observer, corrupted.AsSpan(0, random.Next(corrupted.Length + 1)));
        }
    }
}
