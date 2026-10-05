using System.Buffers;
using System.Text;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class LimitsTests
{
    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        Relative(b => b.Match("password").MaskAny("***"), BlockList));

    [Fact]
    public void MaxValueBytes_CutValue_ReportsTruncatedWithWholeDocument()
    {
        var (result, output) = BytesApiTests.Mask(Observer, """{"note":"abcdefghij","n":1}""", new JsonObserverOptions(MaxValueBytes: 5));

        result.Status.Should().Be(MaskStatus.Truncated);
        result.FailedAtByte.Should().Be(-1);
        output.Should().Be("{\"note\":\"abcde…\",\"n\":1}");
    }

    [Fact]
    public void MaxValueBytes_NoValueCut_ReportsMasked()
        => BytesApiTests.Mask(Observer, """{"note":"abcde"}""", new JsonObserverOptions(MaxValueBytes: 5)).Result.Status.Should().Be(MaskStatus.Masked);

    [Fact]
    public void MaxValueBytes_StrategyOutput_IsNotCut()
    {
        var observer = JsonObserver.Obj(b => b.Match("a").MaskStr((_, _) => new string('x', 100)), BlockList);

        var (result, output) = BytesApiTests.Mask(observer, """{"a":"v"}""", new JsonObserverOptions(MaxValueBytes: 4));

        result.Status.Should().Be(MaskStatus.Masked);
        output.Should().Be($"{{\"a\":\"{new string('x', 100)}\"}}");
    }

    [Fact]
    public void MaxValueBytes_LongCustomRuleString_SurrogatePairNotSplit()
    {
        var observer = JsonObserver.Obj(
            b => b.Match("a").MaskValue((ref Utf8JsonReader _, JsonWriter w, JsonObserveringEmptyContext _, ref PropertyPath _) => w.WriteStringValue("ab\U0001F600cd")),
            BlockList);

        var (_, output) = BytesApiTests.Mask(observer, """{"a":"v"}""", new JsonObserverOptions(MaxValueBytes: 4));

        JsonDocument.Parse(output).RootElement.GetProperty("a").GetString().Should().Be("ab…");
    }

    [Fact]
    public void MaskAny_Strategy_HugeValue_ReceivesTheWholeValue()
    {
        var secret = new string('s', 5 * 1024 * 1024);
        var utf8 = Encoding.UTF8.GetBytes($$"""{"password":"{{secret}}","n":1}""");
        var observer = JsonObserver.Obj(Relative(b => b.Match("password").MaskAny((v, _) => v is null ? null : "len:" + v.Length), BlockList));
        var output = new ArrayBufferWriter<byte>(1024);

        var result = observer.Mask(utf8, output, new JsonObserverOptions(MaxValueBytes: 256));

        result.Status.Should().Be(MaskStatus.Masked);
        Encoding.UTF8.GetString(output.WrittenSpan).Should().Be($$"""{"password":"len:{{secret.Length}}","n":1}""");
    }

    [Fact]
    public void MaxOutputBytes_Reached_StopsReadingTheInput()
    {
        var builder = new StringBuilder("[");
        for (var i = 0; i < 10_000; i++)
        {
            builder.Append(i == 0 ? "" : ",").Append("""{"user":"x"}""");
        }

        var counter = new Counter();
        var observer = JsonObserver.Array<Counter>(a => a.Obj(o => o.Match("user").ReadStr((_, c) => c.Count++)));
        var utf8 = Encoding.UTF8.GetBytes(builder.Append(']').ToString());

        var result = observer.Mask(utf8, new ArrayBufferWriter<byte>(), counter, new JsonObserverOptions(MaxOutputBytes: 100));

        result.Status.Should().Be(MaskStatus.Truncated);
        counter.Count.Should().BeLessThan(20);
    }

    [Theory]
    [InlineData("1e2")]
    [InlineData("3000000000")]
    [InlineData("1.5")]
    public void ReadInt_NonInteger_KeepsTokenAndReadsNull(string number)
    {
        var context = new Counter { Value = 7 };
        var observer = JsonObserver.Obj<Counter>(
            b => b.Match("n").ReadInt((v, c) => c.Value = v),
            JsonObserverValuePolicies<Counter>.Relative(r => r.Match("password").MaskAny("***"), JsonObserverValuePolicies<Counter>.BlockList));
        var json = $$"""{"n":{{number}},"password":"p"}""";

        var output = new ArrayBufferWriter<byte>();
        var result = observer.Mask(Encoding.UTF8.GetBytes(json), output, context);

        result.Status.Should().Be(MaskStatus.Masked);
        Encoding.UTF8.GetString(output.WrittenSpan).Should().Be($$"""{"n":{{number}},"password":"***"}""");
        context.Value.Should().BeNull();
    }

    [Fact]
    public void MaskLong_NonInteger_DoesNotInvalidateBody()
    {
        var observer = JsonObserver.Obj(b => b.Match("n").MaskLong((v, _) => v is null ? "?" : "#"), BlockList);

        var (result, output) = BytesApiTests.Mask(observer, """{"n":1e400,"m":2}""");

        result.Status.Should().Be(MaskStatus.Masked);
        output.Should().Be("""{"n":"?","m":2}""");
    }

    public sealed class Counter
    {
        public int Count { get; set; }
        public int? Value { get; set; }
    }
}
