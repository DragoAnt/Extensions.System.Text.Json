using System.Globalization;
using System.Text.RegularExpressions;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class RuleCoverageTests
{
    [Fact]
    public void RootArrayFactory_RulesAppliedToItems()
        => JsonObserver.Array(a => a.Obj(o => o.Match("id").Unmasked()), NullList)
            .Mask("""[{"id":1,"name":"x"},2,"s"]""").Should().Be("""[{"id":1,"name":null},null,null]""");

    [Fact]
    public void RootArrayFactory_ObjectRoot_Invalid()
    {
        JsonObserver.Array(_ => { }, BlockList).Mask("""{"a":1}""", out var result);

        result.Status.Should().Be(MaskStatus.Invalid);
    }

    [Fact]
    public void ObjFactory_PolicyOnly_AppliesToEveryValue()
        => JsonObserver.Obj(NullList).Mask("""{"a":1,"b":{"c":"x"}}""").Should().Be("""{"a":null,"b":{"c":null}}""");

    [Fact]
    public void TypedStrategies_ReceiveParsedValues()
    {
        var observer = JsonObserver.Obj(b => b
                .Match("i").MaskInt((v, _) => $"i{v}")
                .Match("l").MaskLong((v, _) => $"l{v}")
                .Match("d").MaskDecimal((v, _) => "d" + v?.ToString(CultureInfo.InvariantCulture))
                .Match("b").MaskBool((v, _) => $"b{v}"),
            BlockList);

        observer.Mask("""{"i":7,"l":9007199254740993,"d":1.50,"b":true}""")
            .Should().Be("""{"i":"i7","l":"l9007199254740993","d":"d1.50","b":"bTrue"}""");
    }

    [Fact]
    public void TypedStrategies_NullResult_WritesNull()
        => JsonObserver.Obj(b => b.Match("i").MaskInt((_, _) => null), BlockList).Mask("""{"i":7}""").Should().Be("""{"i":null}""");

    [Fact]
    public void PropMatchesRegex_MatchesName()
        => JsonObserver.Obj(b => b.Match(PropMatches.Regex(new Regex("^pass", RegexOptions.IgnoreCase))).MaskStr("***"), BlockList)
            .Mask("""{"Password":"p","passcode":"c","bypass":"b"}""").Should().Be("""{"Password":"***","passcode":"***","bypass":"b"}""");

    [Fact]
    public void PropMatchesOneOf_InAbsoluteRule()
        => JsonObserver.Obj(b => b.Match(PropMatches.OneOf("pin", "cvv")).MaskAny("***"), BlockList)
            .Mask("""{"PIN":1,"cvv":2,"n":3}""").Should().Be("""{"PIN":"***","cvv":"***","n":3}""");

    [Fact]
    public void StringMaskingStrategy_RegexReplacementAndEvaluator()
    {
        var digits = new Regex("[0-9]");
        var replaced = JsonObserver.Obj(b => b.Match("a").MaskStr(StringMaskingStrategy<JsonObserveringEmptyContext>.Regex(digits, "#")), BlockList);
        var evaluated = JsonObserver.Obj(
            b => b.Match("a").MaskStr(StringMaskingStrategy<JsonObserveringEmptyContext>.Regex(digits, m => m.Index < 2 ? m.Value : "*")),
            BlockList);
        var implicitRegex = JsonObserver.Obj(b => b.Match("a").MaskStr(digits), BlockList);

        replaced.Mask("""{"a":"ab12"}""").Should().Be("""{"a":"ab##"}""");
        evaluated.Mask("""{"a":"1234"}""").Should().Be("""{"a":"12**"}""");
        implicitRegex.Mask("""{"a":"x9"}""").Should().Be("""{"a":"x*"}""");
        implicitRegex.Mask("""{"a":null}""").Should().Be("""{"a":null}""");
    }

    [Fact]
    public void CustomDelegate_PropertyPathApi()
    {
        var seen = new List<string>();
        var observer = JsonObserver.Obj(Relative(b => b.Match("c").MaskValue((ref Utf8JsonReader _, JsonWriter writer, JsonObserveringEmptyContext _, ref PropertyPath path) =>
        {
            seen.Add($"{path.Length}|{path.GetPropertyName(0)}|{path.GetPropertyNameReverse(0)}|{path.GetPropertyNameReverse(1)}|{path.ToString()}|{path.GetPropertyName(9)}");
            writer.WriteStringValue("x");
        }), BlockList));

        observer.Mask("""{"a":{"b":[{"c":1}]}}""").Should().Be("""{"a":{"b":[{"c":"x"}]}}""");
        seen.Should().Equal("4|a|c||a.b[0].c|");
    }

    [Fact]
    public void Unmasked_UnderAllowList_NestedAbsoluteRule()
        => JsonObserver.Obj(b => b.Match("a").Obj(a => a.Match("id").Unmasked()))
            .Mask("""{"a":{"id":5,"name":"x","ok":true},"b":1}""").Should().Be("""{"a":{"id":5,"name":"***","ok":"***"},"b":"***"}""");

    [Fact]
    public void NullList_KeepsStructure()
        => JsonObserver.Obj(NullList).Mask("""{"s":"x","n":1,"b":true,"z":null,"a":[1]}""")
            .Should().Be("""{"s":null,"n":null,"b":null,"z":null,"a":[null]}""");

    [Fact]
    public void Last4_LongStringAndNumber_ShowTail()
    {
        var observer = JsonObserver.Obj(Relative(b => b.Match("c").MaskAny(MaskTag.Last4), BlockList));

        observer.Mask("""{"c":"4111111111111111"}""").Should().Be("""{"c":"***1111"}""");
        observer.Mask("""{"c":4111111111111111}""").Should().Be("""{"c":"***1111"}""");
        observer.Mask("""{"c":"41111111111\u0031\u0032"}""").Should().Be("""{"c":"***1112"}""");
    }

    [Theory]
    [InlineData("1234567", "***")]
    [InlineData("12345678", "***5678")]
    public void Last4_ShortValue_MaskedFully(string value, string expected)
        => JsonObserver.Obj(Relative(b => b.Match("c").MaskAny(MaskTag.Last4), BlockList))
            .Mask($$"""{"c":"{{value}}"}""").Should().Be($$"""{"c":"{{expected}}"}""");

    [Fact]
    public void Hash_NumberLiteralAndStringOfSameText_Equal()
    {
        var observer = JsonObserver.Obj(Relative(b => b.Match("h").MaskAny(MaskTag.Hash), BlockList));
        var options = new JsonObserverOptions(HashKey: "k"u8.ToArray());

        var number = observer.Mask("""{"h":1}""", options);
        var text = observer.Mask("""{"h":"1"}""", options);
        var other = observer.Mask("""{"h":"2"}""", options);

        number.Should().Be(text);
        other.Should().NotBe(text);
    }

    [Fact]
    public void Omit_InArray_WritesNull()
        => JsonObserver.Array(a => a.MaskAny(MaskTag.Omit)).Mask("""["secret",1,{"a":1}]""").Should().Be("[null,null,null]");

    [Fact]
    public void CustomUtf8MaskStrategy_ThatThrows_InvalidNoLeak()
    {
        var observer = JsonObserver.Obj(Relative(b => b.Match("p").MaskAny(MaskTag.Full), BlockList));

        var output = observer.Mask("""{"a":1,"p":"secret","b":2}""", out var result, new JsonObserverOptions(MaskStrategy: new ThrowingStrategy()));

        result.Status.Should().Be(MaskStatus.Invalid);
        output.Should().Be("""{"a":1}""");
    }

    private sealed class ThrowingStrategy : Utf8MaskStrategy
    {
        public override void Mask(ReadOnlySpan<byte> value, JsonTokenType tokenType, MaskTag tag, JsonWriter writer, JsonObserverOptions options)
            => throw new InvalidOperationException("boom");
    }
}
