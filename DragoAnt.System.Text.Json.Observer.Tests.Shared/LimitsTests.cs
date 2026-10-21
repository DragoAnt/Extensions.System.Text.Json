using System.Buffers;
using System.Text;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class LimitsTests
{
    [Fact]
    public void MaxMaskValueBytes_Default_ChangesNothing()
    {
        var output = JsonObserver.Obj(AnyDepth(b => b.Match("card").Mask(MaskTag.Last4), BlockList))
            .Mask("""{"card":"4111111111111111"}""", out var result);
        output.Should().Be("""{"card":"***1111"}""");
        result.Should().Match<MaskResult>(r => r.Status == MaskStatus.Masked && r.Flags == MaskFlags.None);
    }

    [Theory]
    [InlineData("""{"card":"4111111111111111"}""", 15, """{"card":"***"}""")]
    [InlineData("""{"card":"4111111111111111"}""", 16, """{"card":"***1111"}""")]
    [InlineData("""{"card":1234567890123456}""", 15, """{"card":"***"}""")]
    public void MaxMaskValueBytes_LongerValue_IsStarsAndFlagged(string json, int limit, string expected)
    {
        var strategy = new CountingStrategy();
        var observer = JsonObserver.Obj(AnyDepth(b => b.Match("card").Mask(MaskTag.Last4), BlockList));
        var output = observer.Mask(json, out var result, new JsonObserverOptions { MaxMaskValueBytes = limit, Strategy = strategy });
        output.Should().Be(expected);
        var tooLarge = expected.EndsWith("\"***\"}", StringComparison.Ordinal);
        strategy.Calls.Should().Be(tooLarge ? 0 : 1);
        result.Flags.Should().Be(tooLarge ? MaskFlags.ValueTooLarge : MaskFlags.None);
        result.Status.Should().Be(tooLarge ? MaskStatus.Truncated : MaskStatus.Masked);
    }

    [Fact]
    public void MaxMaskValueBytes_IsMeasuredAfterUnescaping()
        => MaxMaskValueBytes_LongerValue_IsStarsAndFlagged(
            """{"card":"\u0034\u0031\u0031\u0031\u0031\u0031\u0031\u0031\u0031\u0031\u0031\u0031\u0031\u0031\u0031\u0031"}""",
            16, """{"card":"***1111"}""");

    [Fact]
    public void MaxMaskValueBytes_SequenceUsesTheSameLimit()
    {
        var json = """{"card":"4111111111111111"}"""u8.ToArray();
        var strategy = new CountingStrategy();
        var observer = JsonObserver.Obj(AnyDepth(b => b.Match("card").Mask(MaskTag.Last4), BlockList));
        var output = new ArrayBufferWriter<byte>();

        var result = observer.Mask(SequenceInputTests.Split(json, 1), output,
            new JsonObserverOptions { MaxMaskValueBytes = 15, Strategy = strategy });

        Encoding.UTF8.GetString(output.WrittenSpan).Should().Be("""{"card":"***"}""");
        strategy.Calls.Should().Be(0);
        result.Status.Should().Be(MaskStatus.Truncated);
        result.Flags.Should().Be(MaskFlags.ValueTooLarge);
    }

    [Fact]
    public void MaxMaskValueBytes_AppliesToTheTaggedUnmatchedPolicy()
    {
        var strategy = new CountingStrategy();
        var observer = JsonObserver.Obj(JsonValuePolicy.AnyDepth(_ => { }, Tagged(MaskTag.Hash)));

        var output = observer.Mask("""{"card":"long"}""", out var result,
            new JsonObserverOptions { MaxMaskValueBytes = 3, Strategy = strategy });

        output.Should().Be("""{"card":"***"}""");
        strategy.Calls.Should().Be(0);
        result.Flags.Should().Be(MaskFlags.ValueTooLarge);
    }

    [Fact]
    public void MaxMaskValueBytes_AppliesToCommentStrategies()
    {
        var strategy = new CountingStrategy();
        var options = new JsonObserverOptions
        {
            MaxMaskValueBytes = 3,
            Strategy = strategy,
            Comments = CommentPolicy.Mask(MaskTag.Hash),
        };

        var output = JsonObserver.Obj(BlockList).Mask("""{"a":1 /* long comment */}""", out var result, options);

        output.Should().Contain("/*****/");
        strategy.Calls.Should().Be(0);
        result.Flags.Should().Be(MaskFlags.ValueTooLarge);
    }

    [Fact]
    public void MaxMaskValueBytes_ZeroKeepsNullButLimitsNonEmptyValues()
    {
        var strategy = new CountingStrategy();
        var observer = JsonObserver.Obj(AnyDepth(b => b.Match(new NameMatch(_ => true)).Mask(MaskTag.Full), BlockList));

        var output = observer.Mask("""{"a":"x","b":null}""", out var result,
            new JsonObserverOptions { MaxMaskValueBytes = 0, Strategy = strategy });

        output.Should().Be("""{"a":"***","b":null}""");
        strategy.Calls.Should().Be(0);
        result.Flags.Should().Be(MaskFlags.ValueTooLarge);
    }

    [Fact]
    public void MaxMaskValueBytes_ZeroLeavesALoneNullUnflagged()
    {
        var strategy = new CountingStrategy();
        var observer = JsonObserver.Obj(AnyDepth(b => b.Match(new NameMatch(_ => true)).Mask(MaskTag.Full), BlockList));

        var output = observer.Mask("""{"b":null}""", out var result,
            new JsonObserverOptions { MaxMaskValueBytes = 0, Strategy = strategy });

        output.Should().Be("""{"b":null}""");
        strategy.Calls.Should().Be(0);
        result.Status.Should().Be(MaskStatus.Masked);
        result.Flags.Should().Be(MaskFlags.None);
    }

    [Fact]
    public void MaxMaskValueBytes_ReadReportsTheSameFlag()
    {
        var observer = JsonObserver.Obj(AnyDepth(b => b.Match("card").Mask(MaskTag.Last4), BlockList));
        var options = new JsonObserverOptions { MaxMaskValueBytes = 15 };

        var masked = observer.Mask("""{"card":"4111111111111111"}""", out var maskResult, options);
        var readResult = observer.Read("""{"card":"4111111111111111"}""", options);

        masked.Should().Be("""{"card":"***"}""");
        readResult.Flags.Should().Be(maskResult.Flags);
        readResult.Status.Should().Be(maskResult.Status);
    }

    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        AnyDepth(b => b.Match("password").Mask("***"), BlockList));

    [Fact]
    public void MaxValueBytes_CutValue_ReportsTruncatedWithWholeDocument()
    {
        var (result, output) = BytesApiTests.Mask(Observer, """{"note":"abcdefghij","n":1}""", new JsonObserverOptions { MaxValueBytes = 5 });

        result.Status.Should().Be(MaskStatus.Truncated);
        result.FailedAtByte.Should().Be(-1);
        output.Should().Be("{\"note\":\"abcde…\",\"n\":1}");
    }

    [Fact]
    public void MaxValueBytes_NoValueCut_ReportsMasked()
        => BytesApiTests.Mask(Observer, """{"note":"abcde"}""", new JsonObserverOptions { MaxValueBytes = 5 }).Result.Status.Should().Be(MaskStatus.Masked);

    [Fact]
    public void MaxValueBytes_StrategyOutput_IsNotCut()
    {
        var observer = JsonObserver.Obj(b => b.Match("a").Mask((_, _) => new string('x', 100), MaskNulls.Mask), BlockList);

        var (result, output) = BytesApiTests.Mask(observer, """{"a":"v"}""", new JsonObserverOptions { MaxValueBytes = 4 });

        result.Status.Should().Be(MaskStatus.Masked);
        output.Should().Be($"{{\"a\":\"{new string('x', 100)}\"}}");
    }

    [Fact]
    public void MaxValueBytes_LongCustomRuleString_SurrogatePairNotSplit()
    {
        var observer = JsonObserver.Obj(
            b => b.Match("a").MaskValue((ref JsonValueContext<NoContext> __c) => { var w = __c.Writer; w.WriteStringValue("ab\U0001F600cd"); }),
            BlockList);

        var (_, output) = BytesApiTests.Mask(observer, """{"a":"v"}""", new JsonObserverOptions { MaxValueBytes = 4 });

        JsonDocument.Parse(output).RootElement.GetProperty("a").GetString().Should().Be("ab…");
    }

    [Fact]
    public void MaskAny_Strategy_HugeValue_ReceivesTheWholeValue()
    {
        var secret = new string('s', 5 * 1024 * 1024);
        var utf8 = Encoding.UTF8.GetBytes($$"""{"password":"{{secret}}","n":1}""");
        var observer = JsonObserver.Obj(AnyDepth(b => b.Match("password").Mask((v, _) => v is null ? null : "len:" + v.Length), BlockList));
        var output = new ArrayBufferWriter<byte>(1024);

        var result = observer.Mask(utf8, output, new JsonObserverOptions { MaxValueBytes = 256 });

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

        var result = observer.Mask(utf8, new ArrayBufferWriter<byte>(), counter, new JsonObserverOptions { MaxOutputBytes = 100 });

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
            JsonValuePolicy.AnyDepth<Counter>(r => r.Match("password").Mask("***"), ValuePolicy.BlockList));
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

    private sealed class CountingStrategy : ValueMaskStrategy
    {
        public int Calls { get; private set; }

        public override void Mask(in MaskContext context, MaskValueWriter output)
        {
            Calls++;
            Default.Mask(context, output);
        }
    }
}
