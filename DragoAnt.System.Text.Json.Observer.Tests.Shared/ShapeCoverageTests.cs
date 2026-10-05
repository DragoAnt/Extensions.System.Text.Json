using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DragoAnt.Observer;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class ShapeCoverageTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    private static JsonObserver Observer<T>(JsonSerializerOptions? options = null, JsonShapeOptions? shapeOptions = null) =>
        JsonObserver.FromShape(JsonShape.FromTypeInfo((options ?? Web).GetTypeInfo(typeof(T)), Classify), shapeOptions);

    private static MaskTag? Classify(JsonPropertyInfo property) =>
        property.AttributeProvider?.GetCustomAttributes(typeof(JsonShapeTests.SensitiveAttribute), true)
            .OfType<JsonShapeTests.SensitiveAttribute>().FirstOrDefault() is { } sensitive
            ? MaskTag.Create(sensitive.Kind)
            : null;

    [Fact]
    public void PositionalRecord_PropertyAttribute_Masked()
        => Observer<Person>().Mask("""{"name":"bob","pin":"1234"}""").Should().Be("""{"name":"bob","pin":"***"}""");

    [Fact]
    public void SourceGeneratedTypeInfo_SameAsReflection()
    {
        var json = """{"name":"bob","pin":"1234","x":1}""";
        var generated = JsonObserver.FromShape(JsonShape.FromTypeInfo(ShapeContext.Default.Person, Classify));

#if NET9_0_OR_GREATER
        generated.Mask(json).Should().Be(Observer<Person>().Mask(json)).And.Be("""{"name":"bob","pin":"***","x":"***"}""");
#else
        ShapeContext.Default.Person.Properties.Should().OnlyContain(p => p.AttributeProvider == null);
        generated.Mask(json).Should().Be("""{"name":"bob","pin":"1234","x":"***"}""");
#endif
    }

    [Fact]
    public void SourceGeneratedTypeInfo_ClassifyByName_WorksOnEveryTargetFramework()
        => JsonObserver.FromShape(JsonShape.FromTypeInfo(ShapeContext.Default.Person, p => p.Name == "pin" ? MaskTag.Full : null))
            .Mask("""{"name":"bob","pin":"1234"}""").Should().Be("""{"name":"bob","pin":"***"}""");

    [Fact]
    public void SnakeCaseNamingPolicy_MatchesConvertedNames()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

        Observer<Card>(options).Mask("""{"card_number":"4111111111111111","holder_name":"bob"}""")
            .Should().Be("""{"card_number":"***","holder_name":"bob"}""");
    }

    [Fact]
    public void Polymorphic_DerivedMembersAndDiscriminatorMasked()
        => Observer<Animal>().Mask("""{"$type":"dog","name":"rex","bark":"loud"}""")
            .Should().Be("""{"$type":"***","name":"rex","bark":"***"}""");

    [Fact]
    public void ExtensionData_OverflowValuesMasked()
        => Observer<WithExtra>().Mask("""{"id":1,"other":"x","nested":{"a":1}}""")
            .Should().Be("""{"id":1,"other":"***","nested":"***"}""");

    [Fact]
    public void NestedCollections_InnerItemsShaped()
        => Observer<Nested>().Mask("""{"groups":[{"k":[{"sku":"A","secretCode":"s","x":1}]}]}""")
            .Should().Be("""{"groups":[{"k":[{"sku":"A","secretCode":"***","x":"***"}]}]}""");

    [Fact]
    public void ReadOnlyDictionaryAndImmutableArray_Shaped()
        => Observer<ReadOnlyCollections>().Mask("""{"byCode":{"K":{"sku":"A","secretCode":"s"}},"list":[{"sku":"B","secretCode":"t"}]}""")
            .Should().Be("""{"byCode":{"K":{"sku":"A","secretCode":"***"}},"list":[{"sku":"B","secretCode":"***"}]}""");

    [Fact]
    public void MutualRecursion_BuildsAndMasks()
        => Observer<NodeA>().Mask("""{"secret":"s","b":{"n":1,"a":{"secret":"t","b":{"n":2,"z":3}}}}""")
            .Should().Be("""{"secret":"***","b":{"n":1,"a":{"secret":"***","b":{"n":2,"z":"***"}}}}""");

    [Fact]
    public void EnumAsString_ShownAsScalar()
        => Observer<WithEnum>().Mask("""{"color":"Red"}""").Should().Be("""{"color":"Red"}""");

    [Fact]
    public void ConcurrentFromShape_SameShape_SameOutput()
    {
        var shape = JsonShape.FromTypeInfo(Web.GetTypeInfo(typeof(Person)), Classify);
        var outputs = new string?[8];

        Parallel.For(0, outputs.Length, i => outputs[i] = JsonObserver.FromShape(shape).Mask("""{"name":"bob","pin":"1"}"""));

        outputs.Should().AllBe("""{"name":"bob","pin":"***"}""");
    }

    [Fact]
    public void AddAfterObserverBuilt_Throws()
    {
        var shape = JsonShape.Object(("id", JsonShape.Scalar));
        JsonObserver.FromShape(shape);

        var add = () => shape.Add("name", JsonShape.Scalar);

        add.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Members_IsReadOnly()
    {
        var shape = JsonShape.Object(("id", JsonShape.Scalar));

        shape.Members.Should().NotBeAssignableTo<List<JsonShapeProperty>>();
        shape.Members.Should().ContainSingle();
    }

    [Fact]
    public void HandBuiltMap_KeysShownValuesShaped()
        => JsonObserver.FromShape(JsonShape.Map(JsonShape.Masked(MaskTag.Full))).Mask("""{"a":1,"b":"x"}""")
            .Should().Be("""{"a":"***","b":"***"}""");

    public sealed record Person(string Name, [property: JsonShapeTests.Sensitive] string Pin);

    public sealed class Card
    {
        [JsonShapeTests.Sensitive]
        public string? CardNumber { get; set; }

        public string? HolderName { get; set; }
    }

    [JsonDerivedType(typeof(Dog), "dog")]
    public class Animal
    {
        public string? Name { get; set; }
    }

    public sealed class Dog : Animal
    {
        public string? Bark { get; set; }
    }

    public sealed class WithExtra
    {
        public int Id { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    public sealed class Nested
    {
        public List<Dictionary<string, JsonShapeTests.Order[]>>? Groups { get; set; }
    }

    public sealed class ReadOnlyCollections
    {
        public IReadOnlyDictionary<string, JsonShapeTests.Order>? ByCode { get; set; }
        public ImmutableArray<JsonShapeTests.Order> List { get; set; }
    }

    public sealed class NodeA
    {
        [JsonShapeTests.Sensitive]
        public string? Secret { get; set; }

        public NodeB? B { get; set; }
    }

    public sealed class NodeB
    {
        public int N { get; set; }
        public NodeA? A { get; set; }
    }

    public enum Color
    {
        Red,
        Green,
    }

    public sealed class WithEnum
    {
        [JsonConverter(typeof(JsonStringEnumConverter<Color>))]
        public Color Color { get; set; }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ShapeCoverageTests.Person))]
internal sealed partial class ShapeContext : JsonSerializerContext;
