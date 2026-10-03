using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class ApiSurfaceTests
{
    [Fact]
    public void ArrayBuilder_EveryRuleKind()
    {
        var context = new Values();
        string Mask(Action<Builders.JsonArrayBuilder<Values>> init, string json)
            => JsonObserver.Array(init, JsonObserverValuePolicies<Values>.BlockList).Mask(json, context)!;

        Mask(a => a.MaskStr("s"), """[1,"x",{"a":1}]""").Should().Be("""["s","s","s"]""");
        Mask(a => a.MaskStr((v, _) => v + "!"), """["x"]""").Should().Be("""["x!"]""");
        Mask(a => a.MaskInt((v, _) => $"{v}"), """[1,"x"]""").Should().Be("""["1",""]""");
        Mask(a => a.MaskLong((v, _) => $"{v}"), """[2]""").Should().Be("""["2"]""");
        Mask(a => a.MaskDecimal((v, _) => $"{v}"), """[3]""").Should().Be("""["3"]""");
        Mask(a => a.MaskBool((v, _) => $"{v}"), """[true]""").Should().Be("""["True"]""");
        Mask(a => a.MaskAny((v, _) => v), """["a",null]""").Should().Be("""["a",null]""");
        Mask(a => a.MaskAny("*"), """[1]""").Should().Be("""["*"]""");
        Mask(a => a.MaskAny(MaskTag.Full), """[1]""").Should().Be("""["***"]""");
        Mask(a => a.MaskRawValue((v, _) => v), """["a\"b"]""").Should().Be("""["a\\\"b"]""");
        Mask(a => a.Array(i => i.MaskStr("n")), """[[1],2]""").Should().Be("""[["n"],2]""");
        Mask(a => a.Unmasked(), """[1,"x"]""").Should().Be("""[1,"x"]""");

        Mask(a => a.ReadStr((v, c) => c.Str = v), """["s"]""").Should().Be("""["s"]""");
        Mask(a => a.ReadInt((v, c) => c.Int = v), """[4]""").Should().Be("""[4]""");
        Mask(a => a.ReadLong((v, c) => c.Long = v), """[5]""").Should().Be("""[5]""");
        Mask(a => a.ReadBool((v, c) => c.Bool = v), """[false]""").Should().Be("""[false]""");
        Mask(a => a.ReadDecimal((v, c) => c.Decimal = v), """[1.5]""").Should().Be("""[1.5]""");
        Mask(a => a.ReadRaw((v, c) => c.Raw = v), """[true]""").Should().Be("""[true]""");

        context.Should().BeEquivalentTo(new Values { Str = "s", Int = 4, Long = 5, Bool = false, Decimal = 1.5m, Raw = "true" });
    }

    [Fact]
    public void RelativeBuilder_EveryReadRule_KeepsValues()
    {
        var context = new Values();
        var observer = JsonObserver.Obj(JsonObserverValuePolicies<Values>.Relative(b => b
                .Match("s").ReadStr((v, c) => c.Str = v)
                .Match("i").ReadInt((v, c) => c.Int = v)
                .Match("l").ReadLong((v, c) => c.Long = v)
                .Match("d").ReadDecimal((v, c) => c.Decimal = v)
                .Match("b").ReadBool((v, c) => c.Bool = v)
                .Match("r").ReadRaw((v, c) => c.Raw = v)
                .Match("m").MaskInt((_, _) => "i")
                .Match("n").MaskLong((_, _) => "l")
                .Match("o").MaskDecimal((_, _) => "d")
                .Match("p").MaskBool((_, _) => "b")
                .Match("q").MaskRawValue((_, _) => "r")
                .Match("u").Unmasked(),
            JsonObserverValuePolicies<Values>.NullList));

        observer.Mask("""{"x":{"s":"a","i":1,"l":2,"d":3.5,"b":true,"r":"z","m":1,"n":2,"o":3,"p":true,"q":"q","u":7,"v":8}}""", context)
            .Should().Be("""{"x":{"s":"a","i":1,"l":2,"d":3.5,"b":true,"r":"z","m":"i","n":"l","o":"d","p":"b","q":"r","u":7,"v":null}}""");
        context.Should().BeEquivalentTo(new Values { Str = "a", Int = 1, Long = 2, Decimal = 3.5m, Bool = true, Raw = "z" });
    }

    [Fact]
    public void ObjBuilder_RawAndAnyFunction()
        => JsonObserver.Obj(b => b.Match("a").MaskRawValue((v, _) => "<" + v + ">").Match("b").MaskAny((v, _) => v + "?"), BlockList)
            .Mask("""{"a":1.0,"b":false}""").Should().Be("""{"a":"<1.0>","b":"false?"}""");

    [Fact]
    public void Read_WithMaskingRules_WritesNothingReadsValues()
    {
        var context = new Values();
        var observer = JsonObserver.Obj<Values>(b => b
            .Match("p").MaskStr("***")
            .Match("t").MaskAny(MaskTag.Last4)
            .Match("n").MaskInt((_, _) => "*")
            .Match("s").ReadStr((v, c) => c.Str = v)
            .Match("o").Obj(o => o.Match("i").ReadInt((v, c) => c.Int = v)), JsonObserverValuePolicies<Values>.BlockList);

        var result = observer.Read("""{"p":"secret","t":{"a":1},"n":5,"x":true,"y":null,"s":"v","o":{"i":3},"z":[1,"a",false,1.5]}""", context);

        result.Should().Be(new MaskResult(MaskStatus.Masked, 0, -1));
        context.Str.Should().Be("v");
        context.Int.Should().Be(3);
    }

    [Fact]
    public void CustomRule_EveryWriterMethod()
    {
        var observer = JsonObserver.Obj(b => b.Match("a").MaskValue((ref Utf8JsonReader _, JsonWriter writer, JsonObserveringEmptyContext _, ref PropertyPath _) =>
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(1L);
            writer.WriteNumberValue(2.5m);
            writer.WriteBooleanValue(true);
            writer.WriteNullValue();
            writer.WriteStringValue((string?)null);
            writer.WriteRawValue("3e1"u8);
            writer.WriteStartObject();
            writer.WritePropertyName("k");
            writer.WriteStringValue("v"u8);
            writer.WritePropertyName("j"u8);
            writer.WriteStringValue("w");
            writer.WriteEndObject();
            writer.WriteEndArray();
        }), BlockList);

        observer.Mask("""{"a":0}""").Should().Be("""{"a":[1,2.5,true,null,null,3e1,{"k":"v","j":"w"}]}""");
        observer.Mask("""{"a":0}""", new JsonObserverOptions(IgnoreNulls: true)).Should().Be("""{"a":[1,2.5,true,3e1,{"k":"v","j":"w"}]}""");
    }

    [Fact]
    public void IgnoreNulls_EveryValueType()
        => JsonObserver.Obj(Relative(b => b.Match("m").MaskStr((_, _) => null), BlockList))
            .Mask("""{"s":"x","n":1.5,"b":false,"z":null,"m":"gone","o":{"a":[true,null,{"q":null}],"e":"é"}}""", new JsonObserverOptions(IgnoreNulls: true))
            .Should().Be("""{"s":"x","n":1.5,"b":false,"o":{"a":[true],"e":"é"}}""");

    [Fact]
    public void IgnoreNulls_DeepNesting_GrowsBuffers()
    {
        var depth = 40;
        var name = new string('n', 300);
        var json = string.Concat(Enumerable.Repeat($$"""{"{{name}}":""", depth)) + "1" + new string('}', depth);

        JsonObserver.Obj(BlockList).Mask(json, new JsonObserverOptions(IgnoreNulls: true)).Should().Be(json);
    }

#pragma warning disable CS0618
    [Fact]
    public void LegacyAllowList_EveryValueType()
        => JsonObserver.Obj(LegacyAllowList).Mask("""{"s":"x","n":1,"t":true,"f":false,"z":null}""")
            .Should().Be("""{"s":"#str#*****","n":"#number#*****","t":true,"f":false,"z":null}""");
#pragma warning restore CS0618

    [Fact]
    public void PropMatchingStrategy_ConvertsToFunction()
    {
        Func<string?, bool> exact = (PropMatchingStrategy)"Pin";
        Func<string?, bool> custom = (PropMatchingStrategy)(Func<string?, bool>)(n => n == "x");

        exact("PIN").Should().BeTrue();
        exact("pins").Should().BeFalse();
        custom("x").Should().BeTrue();
        ((Func<string?, bool>)default(PropMatchingStrategy))("a").Should().BeFalse();
    }

    [Fact]
    public void NonAsciiPatterns_FallBackToStringComparison()
    {
        var observer = JsonObserver.Obj(Relative(b => b
                .Match(PropMatches.StartsWith("пар")).MaskAny("1")
                .Match(PropMatches.EndsWith("оль")).MaskAny("2")
                .Match(PropMatches.Contains("ём")).MaskAny("3")
                .Match("ключ").MaskAny("4"),
            BlockList));

        observer.Mask("""{"пароль":"a","кроль":"b","объём":"c","КЛЮЧ":"d","other":"e"}""")
            .Should().Be("""{"пароль":"1","кроль":"2","объём":"3","КЛЮЧ":"4","other":"e"}""");
    }

    [Fact]
    public void Match_WithoutNames_Throws()
    {
        var build = () => JsonObserver.Obj(b => b.Match().MaskAny("*"));

        build.Should().Throw<ArgumentException>();
    }

    public sealed class Values
    {
        public string? Str { get; set; }
        public int? Int { get; set; }
        public long? Long { get; set; }
        public decimal? Decimal { get; set; }
        public bool? Bool { get; set; }
        public string? Raw { get; set; }
    }
}
