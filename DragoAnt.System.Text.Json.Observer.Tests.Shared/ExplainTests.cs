using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class ExplainTests
{
    private static readonly JsonObserver Lines = JsonObserver.Obj(
        root => root
            .Match("lines").Array(l => l.Obj(x => x.Match("qty").MaskAny("***").Match("note").ReadStr((_, _) => { })))
            .Match("id").Unmasked()
            .Match("custom").MaskValue((ref Utf8JsonReader _, JsonWriter w, JsonObserveringEmptyContext _, ref PropertyPath _) => w.WriteNullValue()),
        BlockList);

    [Fact]
    public void AbsoluteNestedRule_NamesTheChain()
    {
        var explanation = Lines.Explain("lines[0].qty", JsonTokenType.Number);

        explanation.Should().BeEquivalentTo(new JsonPathExplanation(
            "lines[0].qty",
            JsonPathOutcome.Masked,
            """Match("lines") > object item > Match("qty")""",
            """MaskAny("***")""",
            [
                """lines: Match("lines") → Array(...)""",
                "lines[0]: object item → Obj(...)",
                """lines[0].qty: Match("qty") → MaskAny("***")""",
            ]));
        explanation.ToString().Should().Be("""lines[0].qty: Masked by Match("lines") > object item > Match("qty") → MaskAny("***")""");
    }

    [Fact]
    public void RuleKinds_Outcomes()
    {
        Lines.Explain("lines[3].sku").Should().Match<JsonPathExplanation>(e => e.Outcome == JsonPathOutcome.Unchanged && e.Rule == "default policy BlockList");
        Lines.Explain("lines[0].note").Outcome.Should().Be(JsonPathOutcome.Read);
        Lines.Explain("id", JsonTokenType.Number).Outcome.Should().Be(JsonPathOutcome.Unchanged);
        Lines.Explain("custom").Outcome.Should().Be(JsonPathOutcome.Custom);
        Lines.Explain("lines[0].qty", JsonTokenType.Null).Should().Match<JsonPathExplanation>(e => e.Outcome == JsonPathOutcome.Unchanged && e.Action.EndsWith("keeps null"));
        Lines.Explain("other.deep", JsonTokenType.StartObject).Should().Match<JsonPathExplanation>(e => e.Outcome == JsonPathOutcome.Unchanged && e.Action.Contains("descended"));
        Lines.Explain("[0].id").Outcome.Should().Be(JsonPathOutcome.Invalid);
        Lines.Explain("$").Rule.Should().Be("root");
    }

    [Fact]
    public void RelativeRules_AndDefaults()
    {
        var observer = JsonObserver.Obj(Relative(rules => rules
                .Match(PropMatches.EndsWith("card"), "saved", "id").MaskStr("***")
                .Match("card").MaskAny(MaskTag.Last4)
                .Match(PropMatches.Contains("email")).MaskStr((v, _) => v),
            AllowList));

        observer.Explain("s.MY_card.saved.id").Should().Match<JsonPathExplanation>(e =>
            e.Outcome == JsonPathOutcome.Masked && e.Rule == """relative Match(EndsWith("card"), "saved", "id")""" && e.Action == """MaskStr("***")""");
        observer.Explain("a.card.number").Should().Match<JsonPathExplanation>(e =>
            e.Rule == """relative Match("card")""" && e.Action == "MaskAny(MaskTag.Last4) on the whole object");
        observer.Explain("c.workEmail").Action.Should().Be("MaskStr(function)");
        observer.Explain("c.tier").Should().Match<JsonPathExplanation>(e => e.Rule == "default policy AllowList" && e.Action == "writes \"***\"");
        observer.Explain("c.tier", JsonTokenType.Null).Action.Should().Be("keeps null");
        observer.Explain("c.Tier", options: new JsonObserverOptions(PropertyNameCaseInsensitive: false)).Rule.Should().Be("default policy AllowList");
        observer.Explain("c.WORKEMAIL", options: new JsonObserverOptions(PropertyNameCaseInsensitive: false)).Rule.Should().Be("default policy AllowList");
        observer.Explain("c.WORKEMAIL").Rule.Should().StartWith("relative");
    }

    [Fact]
    public void DefaultPolicies_Named()
    {
#pragma warning disable CS0618
        JsonObserver.Obj(LegacyAllowList).Explain("b", JsonTokenType.True).Outcome.Should().Be(JsonPathOutcome.Unchanged);
        JsonObserver.Obj(LegacyAllowList).Explain("s").Action.Should().Be("writes \"#str#*****\"");
        JsonObserver.Obj(LegacyAllowList).Explain("n", JsonTokenType.Number).Action.Should().Be("writes \"#number#*****\"");
#pragma warning restore CS0618
        JsonObserver.Obj(NullList).Explain("s").Action.Should().Be("writes null");
        JsonObserver.Obj((ref Utf8JsonReader _, JsonWriter w, JsonObserveringEmptyContext _, ref PropertyPath _) => w.WriteNullValue())
            .Explain("s").Should().Match<JsonPathExplanation>(e => e.Outcome == JsonPathOutcome.Custom && e.Rule == "custom default policy");
        JsonObserver.Array(BlockList).Explain("[2]", JsonTokenType.Number).Outcome.Should().Be(JsonPathOutcome.Unchanged);
        JsonObserver.Array(a => a.MaskAny("x")).Explain("[0]").Rule.Should().Be("any item");
        JsonObserver.Array(BlockList).Explain("a").Outcome.Should().Be(JsonPathOutcome.Invalid);
    }

    [Fact]
    public void Shape_Explained()
    {
        var observer = JsonShapeTests.Observer();

        observer.Explain("name").Should().Match<JsonPathExplanation>(e => e.Outcome == JsonPathOutcome.Unchanged && e.Rule == "shape Scalar");
        observer.Explain("card").Should().Match<JsonPathExplanation>(e => e.Outcome == JsonPathOutcome.Masked && e.Action == "MaskTag.Last4");
        observer.Explain("password", JsonTokenType.Null).Action.Should().Be("keeps null");
        observer.Explain("orders[1].secretCode").Should().Match<JsonPathExplanation>(e => e.Outcome == JsonPathOutcome.Masked && e.Steps.Count == 4);
        observer.Explain("orders[1].extra").Rule.Should().Be("unknown member (MaskWhole)");
        observer.Explain("byCode.K1.sku").Outcome.Should().Be(JsonPathOutcome.Unchanged);
        observer.Explain("unknown.deep").Should().Match<JsonPathExplanation>(e => e.Outcome == JsonPathOutcome.Masked && e.Rule.Contains("Opaque"));
        observer.Explain("name.first").Rule.Should().Contain("where the path has an object");
        observer.Explain("extra").Rule.Should().Be("shape Opaque");
        observer.Explain("orders", JsonTokenType.StartArray).Outcome.Should().Be(JsonPathOutcome.Unchanged);
        observer.Explain("NAME").Outcome.Should().Be(JsonPathOutcome.Unchanged);
        observer.Explain("NAME", options: new JsonObserverOptions(PropertyNameCaseInsensitive: false)).Outcome.Should().Be(JsonPathOutcome.Masked);
        JsonShapeTests.Observer(shapeOptions: new JsonShapeOptions(UnknownMemberPolicy.Descend)).Explain("unknown.deep.x")
            .Should().Match<JsonPathExplanation>(e => e.Outcome == JsonPathOutcome.Masked && e.Rule == "unknown member (Descend)");
        JsonShapeTests.Observer(shapeOptions: new JsonShapeOptions(UnknownMemberPolicy.PassThrough)).Explain("unknown.deep.x")
            .Outcome.Should().Be(JsonPathOutcome.Unchanged);
        JsonShapeTests.Observer(shapeOptions: new JsonShapeOptions(KeepNulls: false)).Explain("password", JsonTokenType.Null)
            .Outcome.Should().Be(JsonPathOutcome.Masked);
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
        var explain = () => Lines.Explain("a", JsonTokenType.PropertyName);

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
                        JsonValueKind.Number => JsonTokenType.Number,
                        JsonValueKind.Null => JsonTokenType.Null,
                        _ => JsonTokenType.String,
                    };
                    var explanation = observer.Explain(path, kind);
                    var changed = before.GetRawText() != after.GetRawText();
                    (explanation.Outcome == JsonPathOutcome.Masked).Should().Be(changed, explanation.ToString());
                    checkedLeaves++;
                    break;
            }
        }

        Walk(input.RootElement, output.RootElement, "");

        checkedLeaves.Should().BeGreaterThan(20);
    }
}
