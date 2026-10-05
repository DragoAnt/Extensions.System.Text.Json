using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class StringApiTests
{
    private const string Secret = "S3cr3tV4l";

    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        AnyDepth(b => b.Match("password").Mask("***"), BlockList));

    public static TheoryData<string> Inputs =>
    [
        $$"""{"user":"bob","password":"{{Secret}}","a":{"b":[1,2""",
        $$"""{"user":"bob","password":"{{Secret}}","a": }""",
        $$"""{"password":{"v":"{{Secret}}","x":1,"y":""",
        """{"a":""",
        "\"hello\"",
        "123",
        "",
        "   ",
        "[1,2",
        "{\"a\":1}}",
        $$"""{/*c*/"password":"{{Secret}}" // tail""",
        $$"""{"password":"{{Secret}}","list":[1,2,],}""",
        $$"""{"name":"Иван <b>&","esc":"pa\"ss","password":"{{Secret}}"}""",
        "{\"a\":\"" + (char)0x2028 + "\"}",
    ];

    [Theory]
    [MemberData(nameof(Inputs))]
    public void Mask_AnyInput_NeverThrows(string json)
    {
        var mask = () => Observer.Mask(json);

        mask.Should().NotThrow();
        mask()!.Should().NotContain(Secret);
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void Mask_SameOutputAsBytesApi(string json)
    {
        var bytes = BytesApiTests.Mask(Observer, json).Output;

        Observer.Mask(json).Should().Be(bytes);
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void Read_AnyInput_NeverThrows(string json)
    {
        var observer = JsonObserver.Obj<Counter>(b => b.Match("user").ReadStr((_, c) => c.Count++));

        var read = () => observer.Read(json, new Counter());

        read.Should().NotThrow();
    }

    [Fact]
    public void Mask_TruncatedInput_ReturnsSafePrefix()
    {
        Observer.Mask($$"""{"user":"bob","password":"{{Secret}}","a":{"b":[1,2""")
            .Should().Be("""{"user":"bob","password":"***","a":{"b":[1]}}""");
    }

    [Fact]
    public void Mask_InvalidUtf16Surrogate_NeverThrows()
    {
        var mask = () => Observer.Mask("{\"a\":\"\uD800\",\"password\":\"" + Secret + "\"}");

        mask.Should().NotThrow();
        mask()!.Should().NotContain(Secret);
    }

    [Theory]
    [InlineData("""{"password":"x"}""", MaskStatus.Masked, -1)]
    [InlineData("""{"password":"x","a":[1""", MaskStatus.Truncated, 21)]
    [InlineData("""{"a": }""", MaskStatus.Invalid, 6)]
    [InlineData("42", MaskStatus.Unrecognized, 0)]
    public void Mask_OutResult_ReportsStatus(string json, MaskStatus status, long failedAt)
    {
        var output = Observer.Mask(json, out var result);

        result.Status.Should().Be(status);
        result.FailedAtByte.Should().Be(failedAt);
        result.BytesWritten.Should().Be(global::System.Text.Encoding.UTF8.GetByteCount(output!));
    }

    [Fact]
    public void Mask_Null_ReturnsNullAndNotJson()
    {
        Observer.Mask(null, out var result).Should().BeNull();
        result.Status.Should().Be(MaskStatus.Unrecognized);
    }

    [Fact]
    public void Mask_UsesOptions_LikeBytesApi()
    {
        var observer = JsonObserver.Obj(AnyDepth(b => b.Match("h").Mask(MaskTag.Hash), BlockList));
        var options = new JsonObserverOptions { HashKey = "k"u8.ToArray(), MaxValueBytes = 3 };
        const string json = """{"h":"secret","note":"abcdef"}""";

        var text = observer.Mask(json, out var result, options);

        text.Should().Be(BytesApiTests.Mask(observer, json, options).Output);
        result.Status.Should().Be(MaskStatus.Truncated);
        result.FailedAtByte.Should().Be(-1);
    }

    [Fact]
    public void IgnoreNulls_DropsNullPropertiesItemsAndEmptiedContainers()
        => JsonObserver.Obj(BlockList).Mask(
                """{"a":null,"b":{"c":null},"d":[null,1,null],"e":[null],"f":{"g":{"h":null}},"i":"x"}""",
                new JsonObserverOptions { IgnoreNulls = true })
            .Should().Be("""{"d":[1],"i":"x"}""");

    [Fact]
    public void IgnoreNulls_AllNull_KeepsEmptyRoot()
        => JsonObserver.Obj(BlockList).Mask("""{"a":null}""", new JsonObserverOptions { IgnoreNulls = true }).Should().Be("{}");

    [Fact]
    public void IgnoreNulls_OmitTag_DropsProperty()
        => JsonObserver.Obj(AnyDepth(b => b.Match("p").Mask(MaskTag.Null), BlockList))
            .Mask("""{"p":"x","q":1}""", new JsonObserverOptions { IgnoreNulls = true }).Should().Be("""{"q":1}""");

    [Fact]
    public void Indented_TruncatedOutput_StillValidJson()
    {
        var output = Observer.Mask($$"""{"user":"bob","password":"{{Secret}}","a":{"b":[1,2""", new JsonObserverOptions { Indented = true });

        output.Should().Contain(Environment.NewLine).And.NotContain(Secret);
        JsonDocument.Parse(output!).RootElement.GetProperty("a").GetProperty("b").GetArrayLength().Should().Be(1);
    }

    [Theory]
    [InlineData("""{"user":"bob"}""", MaskStatus.Masked, 1)]
    [InlineData("""{"user":"bob","x":""", MaskStatus.Truncated, 1)]
    [InlineData("""{"x": ],"user":"bob"}""", MaskStatus.Invalid, 0)]
    [InlineData("[]", MaskStatus.Invalid, 0)]
    [InlineData("1", MaskStatus.Unrecognized, 0)]
    public void Read_ReportsStatus(string json, MaskStatus status, int count)
    {
        var observer = JsonObserver.Obj<Counter>(b => b.Match("user").ReadStr((_, c) => c.Count++));
        var counter = new Counter();

        observer.Read(json, counter).Status.Should().Be(status);
        counter.Count.Should().Be(count);
    }

    public sealed class Counter
    {
        public int Count { get; set; }
    }
}
