using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class WholeValueMaskingTests
{
    private static readonly JsonObserver Relatively = JsonObserver.Any(
        _ => { },
        _ => { },
        Relative(b => b.Match("secret").MaskAny("***"), BlockList));

    private static readonly JsonObserver Absolutely = JsonObserver.Obj(
        b => b.Match("secret").MaskAny("***").Match("list").Array(a => a.MaskAny("***")),
        BlockList);

    [Fact]
    public void SensitiveNumber_Masked() =>
        Relatively.Mask("""{"secret":987654321,"n":1}""").Should().Be("""{"secret":"***","n":1}""");

    [Fact]
    public void SensitiveBool_Masked() =>
        Relatively.Mask("""{"secret":true,"b":false}""").Should().Be("""{"secret":"***","b":false}""");

    [Fact]
    public void SensitiveObject_MaskedWhole() =>
        Relatively.Mask("""{"a":{"secret":{"value":"x","deep":{"secret":1}},"b":1}}""").Should().Be("""{"a":{"secret":"***","b":1}}""");

    [Fact]
    public void SensitiveArray_MaskedWhole() =>
        Relatively.Mask("""[{"secret":["x",{"y":2}],"b":[1]}]""").Should().Be("""[{"secret":"***","b":[1]}]""");

    [Fact]
    public void SensitiveNull_StaysNull() =>
        Relatively.Mask("""{"secret":null}""").Should().Be("""{"secret":null}""");

    [Fact]
    public void AbsoluteRule_MasksAnyValue() =>
        Absolutely.Mask("""{"secret":{"x":1},"list":[1,"a",true,{"z":1},[2],null],"other":2}""")
            .Should().Be("""{"secret":"***","list":["***","***","***","***","***",null],"other":2}""");

    [Fact]
    public void Strategy_ReceivesScalarTextAndNullForContainer()
    {
        var observer = JsonObserver.Obj(_ => { }, Relative(b => b.Match("v").MaskAny((v, _) => $"[{v ?? "container"}]"), BlockList));

        observer.Mask("""{"a":{"v":42},"b":{"v":"text"},"c":{"v":false},"d":{"v":[1]}}""")
            .Should().Be("""{"a":{"v":"[42]"},"b":{"v":"[text]"},"c":{"v":"[false]"},"d":{"v":"[container]"}}""");
    }

    [Fact]
    public void MaskRawValue_SensitiveBool_Masked()
    {
        var observer = JsonObserver.Obj(_ => { }, Relative(b => b.Match("pin").MaskRawValue((_, _) => "***"), BlockList));

        observer.Mask("""{"pin":true}""").Should().Be("""{"pin":"***"}""");
    }
}
