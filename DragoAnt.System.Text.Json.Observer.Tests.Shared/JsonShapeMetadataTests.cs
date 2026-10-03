using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DragoAnt.System.Text.Json.Observer.Strategies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract partial class JsonShapeMetadataTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    private static JsonShape Build(Action<JsonShapeProperty>? annotate = null) =>
        JsonShape.FromTypeInfo(Web.GetTypeInfo(typeof(Order)), p => p.Name == "secret" ? MaskTag.Full : null, annotate);

    private static JsonShapeProperty Member(JsonShape shape, string name) => shape.Members.Single(m => m.Name == name);

    [Fact]
    public void FromTypeInfo_PropertiesCarryClrMetadata()
    {
        var shape = Build();

        var sku = Member(shape, "sku");
        sku.PropertyInfo.Should().NotBeNull();
        sku.PropertyType.Should().Be(typeof(string));
        sku.Member.Should().BeAssignableTo<PropertyInfo>().Which.Name.Should().Be(nameof(Order.Sku));
        sku.AttributeProvider.Should().BeSameAs(sku.Member);
        sku.IsRequired.Should().BeTrue();
        sku.IsNullable.Should().BeFalse();
        sku.GetCustomAttributes<NoteAttribute>().Select(a => a.Text).Should().Equal("stock keeping unit");

        Member(shape, "note").IsNullable.Should().BeTrue();
        Member(shape, "note").IsRequired.Should().BeFalse();
        Member(shape, "quantity").IsNullable.Should().BeFalse();
        Member(shape, "discount").IsNullable.Should().BeTrue();
        Member(shape, "ext_ref").Member!.Name.Should().Be(nameof(Order.ExternalReference));
        Member(shape, "secret").Shape.Kind.Should().Be(JsonShapeKind.Masked);
        Member(shape, "secret").GetCustomAttributes<NoteAttribute>().Select(a => a.Text).Should().Equal("hidden");
    }

    [Fact]
    public void FromTypeInfo_NodesCarryClrType()
    {
        var shape = Build();

        shape.ClrType.Should().Be(typeof(Order));
        shape.TypeInfo!.Type.Should().Be(typeof(Order));
        Member(shape, "lines").Shape.ClrType.Should().Be(typeof(List<Line>));
        Member(shape, "lines").Shape.Item!.ClrType.Should().Be(typeof(Line));
        Member(shape, "byCode").Shape.Kind.Should().Be(JsonShapeKind.Map);
        Member(shape, "quantity").Shape.Kind.Should().Be(JsonShapeKind.Scalar);
        Member(shape, "quantity").Shape.ClrType.Should().Be(typeof(int));
        JsonShape.Scalar.ClrType.Should().BeNull();
    }

    [Fact]
    public void FromTypeInfo_AnnotateHook_SeesEveryProperty()
    {
        var seen = new List<string>();
        var shape = Build(p =>
        {
            seen.Add($"{p.DeclaringType?.Name}.{p.Name}");
            p.Annotations.Set(new Rule(p.IsRequired));
        });

        seen.Should().Contain(["Order.sku", "Order.lines", "Line.qty"]);
        Member(shape, "sku").Annotations.Get<Rule>().Should().Be(new Rule(true));
        Member(shape, "lines").Shape.Item!.Members.Single(m => m.Name == "qty").Annotations.TryGet<Rule>(out var rule).Should().BeTrue();
        rule.Should().Be(new Rule(false));
    }

    [Fact]
    public void Annotations_SetGetRemove()
    {
        var annotations = JsonShape.Object().Annotations;

        annotations.TryGet<Rule>(out _).Should().BeFalse();
        annotations.Get<Rule>().Should().BeNull();
        annotations.Set(new Rule(true));
        annotations.Set("label");
        annotations.Set(new Rule(false));
        annotations.Count.Should().Be(2);
        annotations.Get<Rule>().Should().Be(new Rule(false));
        annotations.Get<string>().Should().Be("label");
        annotations.Remove<Rule>().Should().BeTrue();
        annotations.Remove<Rule>().Should().BeFalse();
        annotations.Count.Should().Be(1);
        var setNull = () => annotations.Set<string>(null!);
        setNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HandBuiltShape_PropertiesHaveNoClrMetadata()
    {
        var property = new JsonShapeProperty("id", JsonShape.Scalar);
        property.Annotations.Set(new Rule(true));
        var shape = JsonShape.Object(("name", JsonShape.Scalar)).Add(property);

        var (name, node) = shape.Members[0];
        name.Should().Be("name");
        node.Should().BeSameAs(JsonShape.Scalar);
        shape.Members[1].Should().BeSameAs(property);
        property.PropertyInfo.Should().BeNull();
        property.Member.Should().BeNull();
        property.IsNullable.Should().BeNull();
        property.IsRequired.Should().BeFalse();
        property.GetCustomAttributes<NoteAttribute>().Should().BeEmpty();
        JsonObserver.FromShape(shape).Mask("""{"id":1,"name":"a","x":2}""").Should().Be("""{"id":1,"name":"a","x":"***"}""");
    }

    [Fact]
    public void SourceGenerated_MetadataAvailability()
    {
        var shape = JsonShape.FromTypeInfo(SourceGenContext.Default.Line, _ => null);

        var qty = Member(shape, "Qty");
        qty.PropertyType.Should().Be(typeof(int));
        qty.IsNullable.Should().BeFalse();
        var label = Member(shape, "Label");
#if NET9_0_OR_GREATER
        label.IsNullable.Should().BeTrue();
#else
        label.IsNullable.Should().BeNull();
        label.Member.Should().BeNull();
#endif
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class NoteAttribute(string text) : Attribute
    {
        public string Text { get; } = text;
    }

    public sealed record Rule(bool Required);

    public sealed class Order
    {
        [Note("stock keeping unit")]
        public required string Sku { get; set; }

        public string? Note { get; set; }
        public int Quantity { get; set; }
        public decimal? Discount { get; set; }

        [JsonPropertyName("ext_ref")]
        public string? ExternalReference { get; set; }

        [Note("hidden")]
        public string? Secret { get; set; }

        public List<Line>? Lines { get; set; }
        public Dictionary<string, Line>? ByCode { get; set; }
    }

    public sealed class Line
    {
        public int Qty { get; set; }
        public string? Label { get; set; }
    }

    [JsonSerializable(typeof(Line))]
    internal sealed partial class SourceGenContext : JsonSerializerContext;
}
