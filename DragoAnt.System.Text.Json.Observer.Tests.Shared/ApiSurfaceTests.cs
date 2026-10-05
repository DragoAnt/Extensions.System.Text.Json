using DragoAnt.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class ApiSurfaceTests
{
    [Fact]
    public void ArrayBuilder_EveryRuleKind()
    {
        var context = new Values();
        string Mask(Action<Builders.JsonArrayBuilder<Values>> init, string json)
            => JsonObserver.Array(init, ValuePolicy.BlockList).Mask(json, context)!;

        Mask(a => a.Mask("s", MaskNulls.Mask), """[1,"x",{"a":1}]""").Should().Be("""["s","s","s"]""");
        Mask(a => a.Mask((v, _) => v + "!", MaskNulls.Mask), """["x"]""").Should().Be("""["x!"]""");
        Mask(a => a.MaskInt((v, _) => $"{v}"), """[1,"x"]""").Should().Be("""["1",""]""");
        Mask(a => a.MaskLong((v, _) => $"{v}"), """[2]""").Should().Be("""["2"]""");
        Mask(a => a.MaskDecimal((v, _) => $"{v}"), """[3]""").Should().Be("""["3"]""");
        Mask(a => a.MaskBool((v, _) => $"{v}"), """[true]""").Should().Be("""["True"]""");
        Mask(a => a.Mask((v, _) => v), """["a",null]""").Should().Be("""["a",null]""");
        Mask(a => a.Mask("*"), """[1]""").Should().Be("""["*"]""");
        Mask(a => a.Mask(MaskTag.Full), """[1]""").Should().Be("""["***"]""");
        Mask(a => a.Mask((v, _) => v, MaskNulls.Mask), """["a\"b"]""").Should().Be("""["a\"b"]""");
        Mask(a => a.Array(i => i.Mask("n", MaskNulls.Mask)), """[[1],2]""").Should().Be("""[["n"],2]""");
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
        var observer = JsonObserver.Obj(JsonValuePolicy.AnyDepth<Values>(b => b
                .Match("s").ReadStr((v, c) => c.Str = v).Unmasked()
                .Match("i").ReadInt((v, c) => c.Int = v).Unmasked()
                .Match("l").ReadLong((v, c) => c.Long = v).Unmasked()
                .Match("d").ReadDecimal((v, c) => c.Decimal = v).Unmasked()
                .Match("b").ReadBool((v, c) => c.Bool = v).Unmasked()
                .Match("r").ReadRaw((v, c) => c.Raw = v).Unmasked()
                .Match("m").MaskInt((_, _) => "i")
                .Match("n").MaskLong((_, _) => "l")
                .Match("o").MaskDecimal((_, _) => "d")
                .Match("p").MaskBool((_, _) => "b")
                .Match("q").Mask((_, _) => "r", MaskNulls.Mask)
                .Match("u").Unmasked(),
            ValuePolicy.NullList));

        observer.Mask("""{"x":{"s":"a","i":1,"l":2,"d":3.5,"b":true,"r":"z","m":1,"n":2,"o":3,"p":true,"q":"q","u":7,"v":8}}""", context)
            .Should().Be("""{"x":{"s":"a","i":1,"l":2,"d":3.5,"b":true,"r":"z","m":"i","n":"l","o":"d","p":"b","q":"r","u":7,"v":null}}""");
        context.Should().BeEquivalentTo(new Values { Str = "a", Int = 1, Long = 2, Decimal = 3.5m, Bool = true, Raw = "z" });
    }

    [Fact]
    public void ObjBuilder_RawAndAnyFunction()
        => JsonObserver.Obj(b => b.Match("a").Mask((v, _) => "<" + v + ">", MaskNulls.Mask).Match("b").Mask((v, _) => v + "?"), BlockList)
            .Mask("""{"a":1.0,"b":false}""").Should().Be("""{"a":"<1.0>","b":"false?"}""");

    [Fact]
    public void Read_WithMaskingRules_WritesNothingReadsValues()
    {
        var context = new Values();
        var observer = JsonObserver.Obj<Values>(b => b
            .Match("p").Mask("***", MaskNulls.Mask)
            .Match("t").Mask(MaskTag.Last4)
            .Match("n").MaskInt((_, _) => "*")
            .Match("s").ReadStr((v, c) => c.Str = v)
            .Match("o").Obj(o => o.Match("i").ReadInt((v, c) => c.Int = v)), ValuePolicy.BlockList);

        var result = observer.Read("""{"p":"secret","t":{"a":1},"n":5,"x":true,"y":null,"s":"v","o":{"i":3},"z":[1,"a",false,1.5]}""", context);

        result.Should().Be(new MaskResult { Status = MaskStatus.Masked, BytesWritten = 0, FailedAtByte = -1 });
        context.Str.Should().Be("v");
        context.Int.Should().Be(3);
    }

    [Fact]
    public void CustomRule_EveryWriterMethod()
    {
        var observer = JsonObserver.Obj(b => b.Match("a").MaskValue((ref JsonValueContext<NoContext> __c) => { var writer = __c.Writer;
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
        observer.Mask("""{"a":0}""", new JsonObserverOptions { IgnoreNulls = true }).Should().Be("""{"a":[1,2.5,true,3e1,{"k":"v","j":"w"}]}""");
    }

    [Fact]
    public void IgnoreNulls_EveryValueType()
        => JsonObserver.Obj(AnyDepth(b => b.Match("m").Mask((_, _) => null, MaskNulls.Mask), BlockList))
            .Mask("""{"s":"x","n":1.5,"b":false,"z":null,"m":"gone","o":{"a":[true,null,{"q":null}],"e":"é"}}""", new JsonObserverOptions { IgnoreNulls = true })
            .Should().Be("""{"s":"x","n":1.5,"b":false,"o":{"a":[true],"e":"é"}}""");

    [Fact]
    public void IgnoreNulls_DeepNesting_GrowsBuffers()
    {
        var depth = 40;
        var name = new string('n', 300);
        var json = string.Concat(Enumerable.Repeat($$"""{"{{name}}":""", depth)) + "1" + new string('}', depth);

        JsonObserver.Obj(BlockList).Mask(json, new JsonObserverOptions { IgnoreNulls = true }).Should().Be(json);
    }

    [Fact]
    public void NameMatch_TestsDecodedNames()
    {
        NameMatch exact = "Pin";
        var custom = new NameMatch(n => n == "x");

        exact.IsMatch("PIN", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        exact.IsMatch("PIN", StringComparison.Ordinal).Should().BeFalse();
        exact.IsMatch("pins", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        custom.IsMatch("x", StringComparison.Ordinal).Should().BeTrue();
        default(NameMatch).IsMatch("a", StringComparison.Ordinal).Should().BeFalse();
    }

    [Fact]
    public void NonAsciiPatterns_FallBackToStringComparison()
    {
        var observer = JsonObserver.Obj(AnyDepth(b => b
                .Match(Names.StartsWith("пар")).Mask("1")
                .Match(Names.EndsWith("оль")).Mask("2")
                .Match(Names.Contains("ём")).Mask("3")
                .Match("ключ").Mask("4"),
            BlockList));

        observer.Mask("""{"пароль":"a","кроль":"b","объём":"c","КЛЮЧ":"d","other":"e"}""")
            .Should().Be("""{"пароль":"1","кроль":"2","объём":"3","КЛЮЧ":"4","other":"e"}""");
    }

    [Fact]
    public void Match_WithoutNames_Throws()
    {
        var build = () => JsonObserver.Obj(b => b.Path().Mask("*"));

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
