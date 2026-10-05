using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class RelativeNullTests
{
    private static string? Mark(string? value, NoContext _) => value is null ? "was-null" : "x";

    [Fact]
    public void MaskStr_Null_CalledForRelativeLikeAbsolute()
    {
        var absolute = JsonObserver.Obj(root => root.Match("a").Obj(a => a.Match("p").Mask(Mark, MaskNulls.Mask)), BlockList);
        var relative = JsonObserver.Obj(AnyDepth(rules => rules.Match("p").Mask(Mark, MaskNulls.Mask), BlockList));
        const string json = """{"a":{"p":null,"q":null}}""";

        absolute.Mask(json).Should().Be("""{"a":{"p":"was-null","q":null}}""");
        relative.Mask(json).Should().Be(absolute.Mask(json));
    }

    [Fact]
    public void TypedMask_Null_CalledForRelative() =>
        JsonObserver.Obj(AnyDepth(rules => rules
                    .Match("i").MaskInt((v, _) => v is null ? "i-null" : "i")
                    .Match("b").MaskBool((v, _) => v is null ? "b-null" : "b")
                    .Match("r").Mask((v, _) => v is null ? "r-null" : "r", MaskNulls.Mask),
                BlockList))
            .Mask("""{"x":{"i":null,"b":null,"r":null}}""")
            .Should().Be("""{"x":{"i":"i-null","b":"b-null","r":"r-null"}}""");

    [Fact]
    public void MaskAny_Null_StaysNullForRelative() =>
        JsonObserver.Obj(AnyDepth(rules => rules.Match("p").Mask((_, _) => "called"), BlockList))
            .Mask("""{"p":null,"o":{"p":null}}""")
            .Should().Be("""{"p":null,"o":{"p":null}}""");

    [Fact]
    public void ReadStr_Null_ReadForRelative()
    {
        var context = new Holder();
        var observer = JsonObserver.Obj(JsonValuePolicy.AnyDepth<Holder>(
            rules => rules.Match("p").ReadStr((v, c) => c.Calls.Add(v ?? "<null>")),
            ValuePolicy.BlockList));

        observer.Mask("""{"p":null,"o":{"p":"v"}}""", context).Should().Be("""{"p":null,"o":{"p":"v"}}""");
        context.Calls.Should().Equal("<null>", "v");
    }

    [Fact]
    public void Null_NoRelativeRule_KeptByDefaultPolicy() =>
        JsonObserver.Obj(AnyDepth(rules => rules.Match("p").Mask(Mark, MaskNulls.Mask), AllowList))
            .Mask("""{"q":null,"s":"x"}""")
            .Should().Be("""{"q":null,"s":"***"}""");

    public sealed class Holder
    {
        public List<string> Calls { get; } = [];
    }
}
