using DragoAnt.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class ExplainTests
{
    private static readonly JsonObserver Lines = JsonObserver.Obj(
        root => root
            .Match("lines").Array(l => l.Obj(x => x.Match("qty").Mask("***").Match("note").ReadStr((_, _) => { })))
            .Match("id").Unmasked()
            .Match("custom").MaskValue((ref JsonValueContext<NoContext> __c) => { var w = __c.Writer; w.WriteNullValue(); }),
        BlockList);

    [Fact]
    public void AbsoluteNestedRule_NamesTheChain()
    {
        var explanation = Lines.Explain("lines[0].qty", ValueKind.Number);

        explanation.Should().BeEquivalentTo(new PathExplanation
        {
            Path = "lines[0].qty",
            Outcome = PathOutcome.Masked,
            Rule = """Match("lines") > object item > Match("qty")""",
            Action = """Mask("***")""",
            Steps =
            [
                """lines: Match("lines") → Array(...)""",
                "lines[0]: object item → Obj(...)",
                """lines[0].qty: Match("qty") → Mask("***")""",
            ],
        });
        explanation.ToString().Should().Be("""lines[0].qty: Masked by Match("lines") > object item > Match("qty") → Mask("***")""");
    }

    [Fact]
    public void RuleKinds_Outcomes()
    {
        Lines.Explain("lines[3].sku").Should().Match<PathExplanation>(e => e.Outcome == PathOutcome.Unchanged && e.Rule == "default policy BlockList");
        Lines.Explain("lines[0].note").Outcome.Should().Be(PathOutcome.Read);
        Lines.Explain("id", ValueKind.Number).Outcome.Should().Be(PathOutcome.Unchanged);
        Lines.Explain("custom").Outcome.Should().Be(PathOutcome.Custom);
        Lines.Explain("lines[0].qty", ValueKind.Null).Should().Match<PathExplanation>(e => e.Outcome == PathOutcome.Unchanged && e.Action.EndsWith("keeps null"));
        Lines.Explain("other.deep", ValueKind.Object).Should().Match<PathExplanation>(e => e.Outcome == PathOutcome.Unchanged && e.Action.Contains("descended"));
        Lines.Explain("[0].id").Outcome.Should().Be(PathOutcome.Invalid);
        Lines.Explain("$").Rule.Should().Be("root");
    }

    [Fact]
    public void RelativeRules_AndDefaults()
    {
        var observer = JsonObserver.Obj(AnyDepth(rules => rules
                .Path(Names.EndsWith("card"), "saved", "id").Mask("***", MaskNulls.Mask)
                .Match("card").Mask(MaskTag.Last4)
                .Match(Names.Contains("email")).Mask((v, _) => v, MaskNulls.Mask),
            AllowList));

        observer.Explain("s.MY_card.saved.id").Should().Match<PathExplanation>(e =>
            e.Outcome == PathOutcome.Masked && e.Rule == """AnyDepth Path(EndsWith("card"), "saved", "id")""" && e.Action == """Mask("***", MaskNulls.Mask)""");
        observer.Explain("a.card.number").Should().Match<PathExplanation>(e =>
            e.Rule == """AnyDepth Match("card")""" && e.Action == "Mask(MaskTag.Last4) on the whole object");
        observer.Explain("c.workEmail").Action.Should().Be("Mask(function, MaskNulls.Mask)");
        observer.Explain("c.tier").Should().Match<PathExplanation>(e => e.Rule == "default policy AllowList" && e.Action == "writes \"***\"");
        observer.Explain("c.tier", ValueKind.Null).Action.Should().Be("keeps null");
        observer.Explain("c.Tier", options: new JsonObserverOptions { NameCaseInsensitive = false }).Rule.Should().Be("default policy AllowList");
        observer.Explain("c.WORKEMAIL", options: new JsonObserverOptions { NameCaseInsensitive = false }).Rule.Should().Be("default policy AllowList");
        observer.Explain("c.WORKEMAIL").Rule.Should().StartWith("AnyDepth");
    }

    [Fact]
    public void DefaultPolicies_Named()
    {
        JsonObserver.Obj(Tagged(MaskTag.Hash)).Explain("s").Should().Match<PathExplanation>(e => e.Outcome == PathOutcome.Masked && e.Rule == "default policy Tagged(Hash)" && e.Action == "Mask(MaskTag.Hash)");
        JsonObserver.Obj(NullList).Explain("s").Action.Should().Be("writes null");
        JsonObserver.Obj(JsonValuePolicy.Custom((ref JsonValueContext<NoContext> __c) => { var w = __c.Writer; w.WriteNullValue(); }))
            .Explain("s").Should().Match<PathExplanation>(e => e.Outcome == PathOutcome.Custom && e.Rule == "custom default policy");
        JsonObserver.Array(BlockList).Explain("[2]", ValueKind.Number).Outcome.Should().Be(PathOutcome.Unchanged);
        JsonObserver.Array(a => a.Mask("x")).Explain("[0]").Rule.Should().Be("any item");
        JsonObserver.Array(BlockList).Explain("a").Outcome.Should().Be(PathOutcome.Invalid);
    }

    [Fact]
    public void Shape_Explained()
    {
        var observer = JsonShapeTests.Observer();

        observer.Explain("name").Should().Match<PathExplanation>(e => e.Outcome == PathOutcome.Unchanged && e.Rule == "shape Scalar");
        observer.Explain("card").Should().Match<PathExplanation>(e => e.Outcome == PathOutcome.Masked && e.Action == "MaskTag.Last4");
        observer.Explain("password", ValueKind.Null).Action.Should().Be("keeps null");
        observer.Explain("orders[1].secretCode").Should().Match<PathExplanation>(e => e.Outcome == PathOutcome.Masked && e.Steps.Count == 4);
        observer.Explain("orders[1].extra").Rule.Should().Be("unknown member (MaskWhole)");
        observer.Explain("byCode.K1.sku").Outcome.Should().Be(PathOutcome.Unchanged);
        observer.Explain("unknown.deep").Should().Match<PathExplanation>(e => e.Outcome == PathOutcome.Masked && e.Rule.Contains("Opaque"));
        observer.Explain("name.first").Rule.Should().Contain("where the path has an object");
        observer.Explain("extra").Rule.Should().Be("shape Opaque");
        observer.Explain("orders", ValueKind.Array).Outcome.Should().Be(PathOutcome.Unchanged);
        observer.Explain("NAME").Outcome.Should().Be(PathOutcome.Unchanged);
        observer.Explain("NAME", options: new JsonObserverOptions { NameCaseInsensitive = false }).Outcome.Should().Be(PathOutcome.Masked);
        JsonShapeTests.Observer(shapeOptions: new JsonShapeOptions { Unknown = UnknownMemberPolicy.Descend }).Explain("unknown.deep.x")
            .Should().Match<PathExplanation>(e => e.Outcome == PathOutcome.Masked && e.Rule == "unknown member (Descend)");
        JsonShapeTests.Observer(shapeOptions: new JsonShapeOptions { Unknown = UnknownMemberPolicy.PassThrough }).Explain("unknown.deep.x")
            .Outcome.Should().Be(PathOutcome.Unchanged);
        JsonShapeTests.Observer(shapeOptions: new JsonShapeOptions { KeepNulls = false }).Explain("password", ValueKind.Null)
            .Outcome.Should().Be(PathOutcome.Masked);
    }

    [Theory]
    [InlineData("$.a.b", "a.b")]
    [InlineData("a[2][10]", "a[2][10]")]
    [InlineData("$['a.b']['it\\'s'][\"x\"]", "['a.b']['it\\'s'].x")]
    [InlineData("[0].id", "[0].id")]
    public void Path_Normalized(string path, string normalized) =>
        JsonObserver.Any(_ => { }, _ => { }, BlockList).Explain(path).Path.Should().Be(normalized);

    [Theory]
    [InlineData("a..b")]
    [InlineData("a.")]
    [InlineData("a[x]")]
    [InlineData("a[1")]
    [InlineData("a['b]")]
    [InlineData("$a")]
    [InlineData("a[0]b")]
    public void Path_Invalid_Throws(string path)
    {
        var explain = () => Lines.Explain(path);

        explain.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValueKind_NotAValue_Throws()
    {
        var explain = () => Lines.Explain("a", (ValueKind)42);

        explain.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Explanations_AgreeWithMasking_OnGoldenPayload()
    {
        var observer = JsonMaskingTests.GetRequestMasking(BlockList);
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
        using var input = JsonDocument.Parse(JsonMaskingTests.TestJson, options);
        using var output = JsonDocument.Parse(observer.Mask(JsonMaskingTests.TestJson)!);
        var checkedLeaves = 0;

        void Walk(JsonElement before, JsonElement after, string path)
        {
            switch (before.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in before.EnumerateObject())
                    {
                        Walk(property.Value, after.GetProperty(property.Name), path.Length == 0 ? property.Name : $"{path}.{property.Name}");
                    }

                    break;
                default:
                    var kind = before.ValueKind switch
                    {
                        JsonValueKind.Number => ValueKind.Number,
                        JsonValueKind.Null => ValueKind.Null,
                        _ => ValueKind.String,
                    };
                    var explanation = observer.Explain(path, kind);
                    var changed = before.GetRawText() != after.GetRawText();
                    (explanation.Outcome == PathOutcome.Masked).Should().Be(changed, explanation.ToString());
                    checkedLeaves++;
                    break;
            }
        }

        Walk(input.RootElement, output.RootElement, "");

        checkedLeaves.Should().BeGreaterThan(20);
    }
}
