using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class JsonWriterSpanTests
{
    private static JsonObserver Writing(JsonValueRule<NoContext> rule) =>
        JsonObserver.Obj(b => b.Match("a").MaskValue(rule), BlockList);

    private static void EverySpanOverload(ref JsonValueContext<NoContext> context)
    {
        var writer = context.Writer;
        writer.WriteStartObject();
        writer.WritePropertyName("chars".AsSpan());
        writer.WriteStringValue("é\"x".AsSpan());
        writer.WritePropertyName("b64".AsSpan());
        writer.WriteBase64StringValue([1, 2, 3, 250]);
        writer.WritePropertyName("d".AsSpan());
        writer.WriteNumberValue(1.25d);
        writer.WritePropertyName("nan".AsSpan());
        writer.WriteNumberValue(double.NaN);
        writer.WritePropertyName("none".AsSpan());
        writer.WriteStringValue(ReadOnlySpan<char>.Empty);
        writer.WriteEndObject();
    }

    [Fact]
    public void SpanOverloads_StringApi() =>
        Writing(EverySpanOverload).Mask("""{"a":0}""")
            .Should().Be("""{"a":{"chars":"é\"x","b64":"AQID+g==","d":1.25,"nan":"NaN","none":""}}""");

    [Fact]
    public void SpanOverloads_BytesApiMatchesStringApi()
    {
        var (result, output) = BytesApiTests.Mask(Writing(EverySpanOverload), """{"a":0}""");

        result.Status.Should().Be(MaskStatus.Masked);
        output.Should().Be(Writing(EverySpanOverload).Mask("""{"a":0}"""));
    }

    [Fact]
    public void SpanOverloads_IgnoreNulls_WritesPendingNames() =>
        Writing(EverySpanOverload).Mask("""{"a":0}""", new JsonObserverOptions { IgnoreNulls = true })
            .Should().Be("""{"a":{"chars":"é\"x","b64":"AQID+g==","d":1.25,"nan":"NaN","none":""}}""");

    [Fact]
    public void CharSpan_LongerThanMaxValueBytes_Cut()
    {
        var observer = Writing((ref JsonValueContext<NoContext> __c) => { var writer = __c.Writer; writer.WriteStringValue("abcdefgh".AsSpan()); });

        var masked = observer.Mask("""{"a":0}""", out var result, new JsonObserverOptions { MaxValueBytes = 4 });

        masked.Should().Be("""{"a":"abcd…"}""");
        result.Status.Should().Be(MaskStatus.Truncated);
    }

    [Fact]
    public void Base64_LongerThanMaxValueBytes_Cut()
    {
        var observer = Writing((ref JsonValueContext<NoContext> __c) => { var writer = __c.Writer; writer.WriteBase64StringValue([1, 2, 3, 4, 5, 6]); });

        observer.Mask("""{"a":0}""", new JsonObserverOptions { MaxValueBytes = 4 }).Should().Be("""{"a":"AQID…"}""");
    }

    [Fact]
    public void SpanOverloads_ReadOnly_WriteNothing()
    {
        var observer = JsonObserver.Obj<NoContext>(b => b.Match("a").MaskValue(EverySpanOverload), ValuePolicy.BlockList);

        observer.Read("""{"a":0}""", NoContext.Instance).Status.Should().Be(MaskStatus.Masked);
    }
}
