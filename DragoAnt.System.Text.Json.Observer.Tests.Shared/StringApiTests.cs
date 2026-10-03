using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class StringApiTests
{
    private const string Secret = "S3cr3tV4l";

    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        Relative(b => b.Match("password").MaskAny("***"), BlockList));

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

    public sealed class Counter
    {
        public int Count { get; set; }
    }
}
