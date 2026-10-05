using System.Text;
using DragoAnt.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class PropertyPathTests
{
    private static List<string> Collect(string json, Func<JsonValueRule<NoContext>, JsonObserver> build)
    {
        var seen = new List<string>();
        var observer = build((ref JsonValueContext<NoContext> c) =>
        {
            seen.Add(c.Path.ToString());
            c.Writer.WriteStringValue("x");
        });
        observer.Mask(json);
        return seen;
    }

    [Fact]
    public void ToString_KeepsArrayIndices()
    {
        var seen = Collect(
            """{"items":[{"sku":"a"},{"sku":"b"},{"sku":"c","tags":["x","y"]}],"m":[[1,2],[3]]}""",
            rule => JsonObserver.Obj(AnyDepth(b => b.Match(new NameMatch(_ => true)).MaskValue(rule), BlockList)));

        seen.Should().Equal(
            "items[0].sku", "items[1].sku", "items[2].sku", "items[2].tags[0]", "items[2].tags[1]",
            "m[0][0]", "m[0][1]", "m[1][0]");
    }

    [Fact]
    public void ToString_RootArrayAndSpecialNames()
    {
        var seen = Collect(
            """[{"a.b":1,"it's":2,"":3,"x[1]":4,"é":5}]""",
            rule => JsonObserver.Array(root => root.Obj(o => o.Match(new NameMatch(_ => true)).MaskValue(rule)), BlockList));

        seen.Should().Equal("[0]['a.b']", "[0]['it\\'s']", "[0]['']", "[0]['x[1]']", "[0].é");
    }

    [Fact]
    public void Accessors_IndexAndUtf8Name()
    {
        var seen = new List<string>();
        var observer = JsonObserver.Obj(AnyDepth(b => b.Match("sku").MaskValue((ref JsonValueContext<NoContext> c) =>
        {
            var path = c.Path;
            var writer = c.Writer;
            path.TryGetItemIndex(1, out var index).Should().BeTrue();
            path.TryGetItemIndex(0, out _).Should().BeFalse();
            path.TryGetItemIndex(7, out _).Should().BeFalse();
            path.IsItem(1).Should().BeTrue();
            path.IsItem(2).Should().BeFalse();
            path.TryGetName(2, out var name).Should().BeTrue();
            path.TryGetName(1, out _).Should().BeFalse();
            path.TryGetName(-1, out _).Should().BeFalse();
            path.GetName(1).Should().BeNull();
            seen.Add($"{index}:{Encoding.UTF8.GetString(name)}");
            writer.WriteStringValue("x");
        }), BlockList));

        observer.Mask("""{"items":[{"sku":"a"},{"sku":"b"}]}""").Should().Be("""{"items":[{"sku":"x"},{"sku":"x"}]}""");
        seen.Should().Equal("0:sku", "1:sku");
    }

    [Fact]
    public void Matching_ArrayItemSegment_StillOneLevel()
    {
        var anyItem = new NameMatch(n => n is null);

        JsonObserver.Obj(root => root.Path("lines", anyItem, "qty").Mask("***"), BlockList)
            .Mask("""{"lines":[{"qty":1},{"qty":2}],"qty":3}""")
            .Should().Be("""{"lines":[{"qty":"***"},{"qty":"***"}],"qty":3}""");
    }
}
