using DragoAnt.System.Text.Json.Observer.Builders;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class NestedArrayRuleTests
{
    private static readonly PropMatchingStrategy AnyItem = new(_ => true);

    private static void Line(JsonObjBuilder<JsonObserveringEmptyContext> line, bool allowList)
    {
        if (allowList)
        {
            line.Match("sku").Unmasked();
        }
        else
        {
            line.Match("qty").MaskAny("***");
        }
    }

    private static JsonObserverValueDelegate<JsonObserveringEmptyContext> Policy(bool allowList) => allowList ? AllowList : BlockList;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjInPropertyArray_OneLevel_RulesApply(bool allowList)
    {
        var observer = JsonObserver.Obj(root => root.Match("lines").Array(l => l.Obj(x => Line(x, allowList))), Policy(allowList));

        observer.Mask("""{"lines":[{"qty":5,"sku":"A"},{"qty":7,"sku":"B"}]}""")
            .Should().Be("""{"lines":[{"qty":"***","sku":"A"},{"qty":"***","sku":"B"}]}""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjInArrayInPropertyArray_TwoLevels_RulesApply(bool allowList)
    {
        var observer = JsonObserver.Obj(root => root.Match("m").Array(l => l.Array(a => a.Obj(x => Line(x, allowList)))), Policy(allowList));

        observer.Mask("""{"m":[[{"qty":5,"sku":"A"}],[{"qty":6,"sku":"B"}]]}""")
            .Should().Be("""{"m":[[{"qty":"***","sku":"A"}],[{"qty":"***","sku":"B"}]]}""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjInArrayInObjInArray_ThreeLevels_RulesApply(bool allowList)
    {
        var observer = JsonObserver.Obj(
            root => root.Match("a").Obj(a => a
                .Match("orders").Array(o => o.Obj(order => order
                    .Match("lines").Array(l => l.Array(q => q.Obj(x => Line(x, allowList))))))),
            Policy(allowList));

        observer.Mask("""{"a":{"orders":[{"lines":[[{"qty":5,"sku":"A"}]]},{"lines":[[{"qty":6,"sku":"B"}]]}]}}""")
            .Should().Be("""{"a":{"orders":[{"lines":[[{"qty":"***","sku":"A"}]]},{"lines":[[{"qty":"***","sku":"B"}]]}]}}""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjInRootArray_Unchanged(bool allowList)
    {
        var observer = JsonObserver.Array(root => root.Obj(x => x.Match("lines").Array(l => l.Obj(y => Line(y, allowList)))), Policy(allowList));

        observer.Mask("""[{"lines":[{"qty":5,"sku":"A"}]}]""")
            .Should().Be("""[{"lines":[{"qty":"***","sku":"A"}]}]""");
    }

    [Fact]
    public void AnyItemWorkaround_StillMatches()
    {
        var byPath = JsonObserver.Obj(root => root.Match("lines", AnyItem, "qty").MaskAny("***"), BlockList);
        var byRelative = JsonObserver.Obj(Relative(rules => rules.Match("lines", AnyItem, "qty").MaskAny("***"), BlockList));
        const string json = """{"lines":[{"qty":5,"sku":"A"}]}""";

        byPath.Mask(json).Should().Be("""{"lines":[{"qty":"***","sku":"A"}]}""");
        byRelative.Mask(json).Should().Be("""{"lines":[{"qty":"***","sku":"A"}]}""");
    }
}
