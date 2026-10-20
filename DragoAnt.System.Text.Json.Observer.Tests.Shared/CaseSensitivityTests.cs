using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using DragoAnt.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class CaseSensitivityTests
{
    private const string CaseInput = "{\"driverLicense\":\"A1\",\"DRiverLicensE\":\"A2\",\"driverlicense\":\"A3\",\"driver\\u004Cicense\":\"A4\","
        + "\"dr\u0456verLicense\":\"A5\",\"dr\u0130verLicense\":\"A6\"}";

    private static readonly JsonObserverOptions Exact = new() { NameCaseInsensitive = false };

    private static readonly JsonObserver Rules = JsonObserver.Obj(AnyDepth(b => b
            .Match("password").Mask("1")
            .Match(Names.StartsWith("tok")).Mask("2")
            .Match(Names.EndsWith("Card")).Mask("3")
            .Match(Names.Contains("mail")).Mask("4")
            .Match(Names.OneOf("pin", "cvv")).Mask("5")
            .Match("ключ").Mask("6")
            .Match(Names.StartsWith("пар")).Mask("7"),
        BlockList));

    private const string Payload = """{"password":"a","Password":"b","token":"c","Token":"d","myCard":"e","mycard":"f","email":"g","eMail":"h","pin":"i","PIN":"j","ключ":"k","КЛЮЧ":"l","пароль":"m","Пароль":"n"}""";

    [Fact]
    public void Rules_Default_IgnoreCase() =>
        Rules.Mask(Payload).Should().Be(
            """{"password":"1","Password":"1","token":"2","Token":"2","myCard":"3","mycard":"3","email":"4","eMail":"4","pin":"5","PIN":"5","ключ":"6","КЛЮЧ":"6","пароль":"7","Пароль":"7"}""");

    [Fact]
    public void Rules_CaseSensitive_MatchExactNamesOnly() =>
        Rules.Mask(Payload, Exact).Should().Be(
            """{"password":"1","Password":"b","token":"2","Token":"d","myCard":"3","mycard":"f","email":"4","eMail":"h","pin":"5","PIN":"j","ключ":"6","КЛЮЧ":"l","пароль":"7","Пароль":"n"}""");

    [Fact]
    public void AbsoluteRules_CaseSensitive_UnderAllowList_MaskUnmatchedCase() =>
        JsonObserver.Obj(root => root.Match("order").Obj(o => o.Match("id").Unmasked()))
            .Mask("""{"order":{"id":1,"ID":2},"Order":{"id":3}}""", Exact)
            .Should().Be("""{"order":{"id":1,"ID":"***"},"Order":{"id":"***"}}""");

    [Fact]
    public void CustomRule_SeesTheCallsMatchingMode()
    {
        var modes = new List<bool>();
        var observer = JsonObserver.Obj(b => b.Match("a").MaskValue((ref JsonValueContext<NoContext> __c) => { var writer = __c.Writer; var path = __c.Path;
            modes.Add(__c.Options.NameCaseInsensitive);
            writer.WriteNullValue();
        }), BlockList);

        observer.Mask("""{"a":1}""");
        observer.Mask("""{"a":1}""", Exact);

        modes.Should().Equal(true, false);
    }

    [Fact]
    public void Shape_FollowsSerializerOptions()
    {
        var general = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var shape = JsonShape.FromTypeInfo(general.GetTypeInfo(typeof(Person)), p => p.Name == "Secret" ? MaskTag.Full : null);
        const string json = """{"Name":"a","name":"b","Secret":"c","secret":"d"}""";

        JsonObserver.FromShape(shape).Mask(json).Should().Be("""{"Name":"a","name":"b","Secret":"***","secret":"***"}""");
        JsonObserver.FromShape(shape, JsonShapeOptions.FromSerializerOptions(general)).Mask(json)
            .Should().Be("""{"Name":"a","name":"***","Secret":"***","secret":"***"}""");
        JsonObserver.FromShape(shape).Mask(json, Exact).Should().Be("""{"Name":"a","name":"***","Secret":"***","secret":"***"}""");
        JsonObserver.FromShape(shape, new JsonShapeOptions { NameCaseInsensitive = true }).Mask(json, Exact)
            .Should().Be("""{"Name":"a","name":"b","Secret":"***","secret":"***"}""");
        JsonShapeOptions.FromSerializerOptions(new JsonSerializerOptions(JsonSerializerDefaults.Web)).NameCaseInsensitive.Should().BeTrue();
    }

    [Fact]
    public void Shape_CaseSensitive_KeepsNamesDifferingInCase()
    {
        var shape = JsonShape.Object(("id", JsonShape.Scalar), ("ID", JsonShape.Masked(MaskTag.Full)), ("ключ", JsonShape.Scalar));

        JsonObserver.FromShape(shape).Mask("""{"id":1,"ID":2}""").Should().Be("""{"id":"***","ID":"***"}""");
        JsonObserver.FromShape(shape).Mask("""{"id":1,"ID":2,"ключ":3,"КЛЮЧ":4}""", Exact)
            .Should().Be("""{"id":1,"ID":"***","ключ":3,"КЛЮЧ":"***"}""");
        shape.FindMember("Id"u8)!.Name.Should().Be("ID");
        shape.FindMember("Id"u8, propertyNameCaseInsensitive: false).Should().BeNull();
        shape.FindMember("id"u8, propertyNameCaseInsensitive: false)!.Name.Should().Be("id");
        shape.FindMember(Encoding.UTF8.GetBytes("КЛЮЧ"), propertyNameCaseInsensitive: false).Should().BeNull();
        shape.FindMember(Encoding.UTF8.GetBytes("КЛЮЧ"))!.Name.Should().Be("ключ");
    }

    public static TheoryData<string, bool, string> CaseTable() => new()
    {
        { "absolute-match", true, "A1 A2 A3 A4" }, { "absolute-match", false, "A1 A4" },
        { "anydepth-match", true, "A1 A2 A3 A4" }, { "anydepth-match", false, "A1 A4" },
        { "one-of", true, "A1 A2 A3 A4" }, { "one-of", false, "A1 A4" },
        { "starts-with", true, "A1 A2 A3 A4" }, { "starts-with", false, "A1 A4" },
        { "ends-with", true, "A1 A2 A3 A4 A5 A6" }, { "ends-with", false, "A1 A4 A5 A6" },
        { "contains", true, "A1 A2 A3 A4 A5 A6" }, { "contains", false, "A1 A2 A4 A5 A6" },
        { "regex", true, "A1 A2 A3 A4" }, { "regex", false, "A1 A4" },
        { "func-with-comparison", true, "A1 A2 A3 A4" }, { "func-with-comparison", false, "A1 A4" },
        { "func-own-case", true, "A1 A4" }, { "func-own-case", false, "A1 A4" },
        { "shape-passthrough", true, "A1 A2 A3 A4" }, { "shape-passthrough", false, "A1 A4" },
    };

    [Theory, MemberData(nameof(CaseTable))]
    public void CaseTable_MatcherAndOption_MasksExpectedVariants(string matcher, bool caseInsensitive, string masked)
    {
        var observer = CaseObserver(matcher, UnknownMemberPolicy.PassThrough);
        using var output = JsonDocument.Parse(observer.Mask(CaseInput, new JsonObserverOptions { NameCaseInsensitive = caseInsensitive })!);
        var maskedSet = masked.Split(' ').ToHashSet(StringComparer.Ordinal);
        for (var i = 1; i <= 6; i++)
        {
            var expectedMasked = maskedSet.Contains($"A{i}");
            output.RootElement.EnumerateObject().ElementAt(i - 1).Value.GetString()
                .Should().Be(expectedMasked ? "***" : $"A{i}", $"{matcher}, caseInsensitive={caseInsensitive}, A{i}");
        }
    }

    [Fact]
    public void CaseTable_KelvinSign_IsADifferentName()
    {
        var observer = JsonObserver.Obj(AnyDepth(b => b.Match("kelvin").Mask(MaskTag.Full), BlockList));
        observer.Mask("{\"\u212Aelvin\":\"K1\",\"KELVIN\":\"K2\"}").Should().Be("{\"\u212Aelvin\":\"K1\",\"KELVIN\":\"***\"}");
    }

    [Fact]
    public void CaseTable_ExplainCaseSensitive_ReportsTheDefaultPolicy()
    {
        var explanation = CaseObserver("anydepth-match", UnknownMemberPolicy.PassThrough).Explain("DRiverLicensE", options: Exact);
        explanation.Outcome.Should().Be(PathOutcome.Unchanged);
        explanation.Rule.Should().Be("default policy BlockList");
    }

    [Fact]
    public void CaseTable_ShapePassThroughCaseSensitive_PassesTheVariant() =>
        CaseObserver("shape-passthrough", UnknownMemberPolicy.PassThrough).Mask("{\"DRiverLicensE\":\"A2\"}", Exact)
            .Should().Be("{\"DRiverLicensE\":\"A2\"}");

    [Fact]
    public void CaseTable_ShapeMaskWholeCaseSensitive_MasksTheVariant() =>
        CaseObserver("shape", UnknownMemberPolicy.MaskWhole).Mask("{\"DRiverLicensE\":\"A2\"}", Exact)
            .Should().Be("{\"DRiverLicensE\":\"***\"}");

    private static JsonObserver CaseObserver(string matcher, UnknownMemberPolicy unknown)
    {
        if (matcher is "shape" or "shape-passthrough")
        {
            return JsonObserver.FromShape(JsonShape.Object(("driverLicense", JsonShape.Masked(MaskTag.Full))), new JsonShapeOptions { Unknown = unknown });
        }
        var match = matcher switch
        {
            "absolute-match" or "anydepth-match" => (NameMatch)"driverLicense",
            "one-of" => Names.OneOf("driverLicense"),
            "starts-with" => Names.StartsWith("driverLicense"),
            "ends-with" => Names.EndsWith("License"),
            "contains" => Names.Contains("verLic"),
            "regex" => Names.Regex(new Regex("^driverLicense$")),
            "func-with-comparison" => new NameMatch((name, comparison) => string.Equals(name, "driverLicense", comparison)),
            "func-own-case" => new NameMatch(name => name == "driverLicense"),
            _ => throw new ArgumentOutOfRangeException(nameof(matcher)),
        };
        return matcher == "absolute-match"
            ? JsonObserver.Obj(r => r.Match(match).Mask(MaskTag.Full), BlockList)
            : JsonObserver.Obj(AnyDepth(r => r.Match(match).Mask(MaskTag.Full), BlockList));
    }

    public sealed class Person
    {
        public string? Name { get; set; }
        public string? Secret { get; set; }
    }
}
