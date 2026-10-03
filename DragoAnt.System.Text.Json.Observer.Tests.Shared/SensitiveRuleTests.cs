using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class SensitiveRuleTests
{
    private static readonly JsonObserver AbsoluteMaskStr = JsonObserver.Obj(b => b.Match("pin").MaskStr("***"), BlockList);

    private static readonly JsonObserver RelativeMaskStr = JsonObserver.Obj(Relative(b => b.Match("pin").MaskStr("***"), BlockList));

    private static readonly JsonObserver AbsoluteMaskRaw = JsonObserver.Obj(b => b.Match("pin").MaskRawValue((_, _) => "***"), BlockList);

    private static readonly JsonObserver RelativeMaskRaw = JsonObserver.Obj(Relative(b => b.Match("pin").MaskRawValue((_, _) => "***"), BlockList));

    public static TheoryData<string, string> SensitiveValues => new()
    {
        { """{"pin":1234,"n":1}""", """{"pin":"***","n":1}""" },
        { """{"pin":true,"n":1}""", """{"pin":"***","n":1}""" },
        { """{"pin":false,"n":1}""", """{"pin":"***","n":1}""" },
        { """{"pin":"1234","n":1}""", """{"pin":"***","n":1}""" },
        { """{"pin":{"v":"1234","w":[5678]},"n":1}""", """{"pin":"***","n":1}""" },
        { """{"pin":["1234",{"v":5678}],"n":1}""", """{"pin":"***","n":1}""" },
    };

    [Theory]
    [MemberData(nameof(SensitiveValues))]
    public void MaskStr_Absolute_NeverExposesAnyValueType(string json, string expected)
        => AbsoluteMaskStr.Mask(json).Should().Be(expected);

    [Theory]
    [MemberData(nameof(SensitiveValues))]
    public void MaskStr_Relative_NeverExposesAnyValueType(string json, string expected)
        => RelativeMaskStr.Mask(json).Should().Be(expected);

    [Theory]
    [MemberData(nameof(SensitiveValues))]
    public void MaskRawValue_Absolute_NeverExposesAnyValueType(string json, string expected)
        => AbsoluteMaskRaw.Mask(json).Should().Be(expected);

    [Theory]
    [MemberData(nameof(SensitiveValues))]
    public void MaskRawValue_Relative_NeverExposesAnyValueType(string json, string expected)
        => RelativeMaskRaw.Mask(json).Should().Be(expected);

    public static TheoryData<string> TypedRules => ["int", "long", "decimal", "bool"];

    [Theory]
    [MemberData(nameof(TypedRules))]
    public void TypedMask_OtherValueTypes_NeverExposed(string rule)
    {
        var observer = JsonObserver.Obj(b => _ = rule switch
        {
            "int" => b.Match("pin").MaskInt((v, _) => v is null ? "***" : "#"),
            "long" => b.Match("pin").MaskLong((v, _) => v is null ? "***" : "#"),
            "decimal" => b.Match("pin").MaskDecimal((v, _) => v is null ? "***" : "#"),
            _ => b.Match("pin").MaskBool((v, _) => v is null ? "***" : "#"),
        }, BlockList);

        foreach (var value in new[] { "\"1234\"", "{\"v\":\"1234\"}", "[\"1234\"]", rule == "bool" ? "1234" : "true" })
        {
            observer.Mask($$"""{"pin":{{value}},"n":1}""").Should().Be("""{"pin":"***","n":1}""", value);
        }
    }

    [Fact]
    public void MaskStr_StrategyInput_PerValueType()
    {
        var seen = new List<string?>();
        var observer = JsonObserver.Obj(b => b.Match("pin").MaskStr((v, _) =>
        {
            seen.Add(v);
            return "***";
        }), BlockList);

        observer.Mask("""{"pin":"abc"}""");
        observer.Mask("""{"pin":12.50}""");
        observer.Mask("""{"pin":true}""");
        observer.Mask("""{"pin":{"x":1}}""");
        observer.Mask("""{"pin":null}""");

        seen.Should().Equal("abc", "12.50", "true", null, null);
    }

    [Fact]
    public void MaskStr_NullStrategyResult_WritesNull()
        => JsonObserver.Obj(b => b.Match("pin").MaskStr((_, _) => null), BlockList)
            .Mask("""{"pin":[1,2]}""").Should().Be("""{"pin":null}""");

    [Fact]
    public void MaskStr_Truncated_InsideSensitiveContainer_NeverWritesItsContent()
    {
        var (result, output) = BytesApiTests.Mask(AbsoluteMaskStr, """{"pin":{"v":"1234","w":""");

        result.Status.Should().Be(MaskStatus.Truncated);
        output.Should().Be("""{"pin":"***"}""");
    }
}
