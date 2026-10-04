using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DragoAnt.System.Text.Json.Observer.Strategies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class JsonShapeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    internal static JsonObserver Observer(JsonSerializerOptions? options = null, JsonShapeOptions? shapeOptions = null) =>
        JsonObserver.FromShape(
            JsonShape.FromTypeInfo((options ?? Web).GetTypeInfo(typeof(Customer)), Classify),
            shapeOptions);

    private static MaskTag? Classify(JsonPropertyInfo property) =>
        property.AttributeProvider?.GetCustomAttributes(typeof(SensitiveAttribute), true).OfType<SensitiveAttribute>().FirstOrDefault() is { } sensitive
            ? new MaskTag(sensitive.Kind)
            : null;

    [Fact]
    public void AllowList_UnknownScalar_Masked() =>
        Observer().Mask("""{"id":1,"unknown":"x","n":5,"name":"bob"}""")
            .Should().Be("""{"id":1,"unknown":"***","n":"***","name":"bob"}""");

    [Fact]
    public void AllowList_UnknownObject_MaskedWhole() =>
        Observer().Mask("""{"unknown":{"name":"x","id":1},"id":2}""").Should().Be("""{"unknown":"***","id":2}""");

    [Fact]
    public void AllowList_UnknownArray_MaskedWhole() =>
        Observer().Mask("""{"unknown":[{"name":"x"},1],"id":2}""").Should().Be("""{"unknown":"***","id":2}""");

    [Fact]
    public void AllowList_KnownSensitive_Tagged() =>
        Observer().Mask("""{"password":{"a":1},"card":"4111111111111111","pin":1234}""")
            .Should().Be("""{"password":"***","card":"***1111","pin":null}""");

    [Fact]
    public void AllowList_NamingPolicyCamel_Matches()
    {
        Observer().Mask("""{"id":1,"Name":"bob","ACTIVE":true}""").Should().Be("""{"id":1,"Name":"bob","ACTIVE":true}""");
        Observer(new JsonSerializerOptions(JsonSerializerDefaults.General) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }).Mask("""{"Id":1,"Name":"bob"}""")
            .Should().Be("""{"Id":1,"Name":"bob"}""");
    }

    [Fact]
    public void AllowList_JsonPropertyName_Matches() =>
        Observer().Mask("""{"e_mail":"a@b.c","email":"a@b.c"}""").Should().Be("""{"e_mail":"***","email":"***"}""");

    [Fact]
    public void AllowList_ListOfModel_Shaped() =>
        Observer().Mask("""{"orders":[{"sku":"A1","secretCode":"x","extra":1},null],"tags":["a","b"]}""")
            .Should().Be("""{"orders":[{"sku":"A1","secretCode":"***","extra":"***"},null],"tags":["a","b"]}""");

    [Fact]
    public void AllowList_Dictionary_KeysShownValuesShaped() =>
        Observer().Mask("""{"byCode":{"K1":{"sku":"A1","secretCode":"x"},"K2":{"other":true}}}""")
            .Should().Be("""{"byCode":{"K1":{"sku":"A1","secretCode":"***"},"K2":{"other":"***"}}}""");

    [Fact]
    public void AllowList_CyclicType_Builds() =>
        Observer().Mask("""{"referrer":{"name":"a","password":"p","referrer":{"id":3,"unknown":1}}}""")
            .Should().Be("""{"referrer":{"name":"a","password":"***","referrer":{"id":3,"unknown":"***"}}}""");

    [Fact]
    public void AllowList_Bool_Masked() =>
        Observer().Mask("""{"active":true,"flag":false}""").Should().Be("""{"active":true,"flag":"***"}""");

    [Fact]
    public void AllowList_OpaqueAndMismatchedTypes_Masked() =>
        Observer().Mask("""{"extra":{"a":1},"raw":[1],"name":{"first":"x"},"orders":"text","id":null}""")
            .Should().Be("""{"extra":"***","raw":"***","name":"***","orders":"***","id":null}""");

    [Fact]
    public void AllowList_Nulls_KeptOrMasked()
    {
        Observer().Mask("""{"unknown":null,"password":null}""").Should().Be("""{"unknown":null,"password":null}""");
        Observer(shapeOptions: new JsonShapeOptions(KeepNulls: false)).Mask("""{"unknown":null,"password":null,"name":null}""")
            .Should().Be("""{"unknown":"***","password":"***","name":null}""");
    }

    [Fact]
    public void Unknown_Descend_ShowsNamesMasksValues() =>
        Observer(shapeOptions: new JsonShapeOptions(UnknownMemberPolicy.Descend)).Mask("""{"unknown":{"a":"x","b":[1,{"c":true}]}}""")
            .Should().Be("""{"unknown":{"a":"***","b":["***",{"c":"***"}]}}""");

    [Fact]
    public void Unknown_PassThrough_KeepsValuesMasksSensitive() =>
        Observer(shapeOptions: new JsonShapeOptions(UnknownMemberPolicy.PassThrough)).Mask("""{"unknown":{"a":"x","b":[1]},"password":"p"}""")
            .Should().Be("""{"unknown":{"a":"x","b":[1]},"password":"***"}""");

    [Fact]
    public void HandBuiltShape_Masks()
    {
        var node = JsonShape.Object(("id", JsonShape.Scalar), ("secret", JsonShape.Masked(MaskTag.Full)));
        node.Add("child", node);

        JsonObserver.FromShape(node).Mask("""{"id":1,"secret":2,"child":{"id":3,"secret":4,"x":5}}""")
            .Should().Be("""{"id":1,"secret":"***","child":{"id":3,"secret":"***","x":"***"}}""");
        var change = () => node.Add("late", JsonShape.Scalar);
        change.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BytesApi_TruncatedShape_ReturnsSafePrefix()
    {
        var (result, output) = BytesApiTests.Mask(Observer(), """{"id":1,"password":{"v":"S3cr3t","x":""");

        result.Status.Should().Be(MaskStatus.Truncated);
        output.Should().Be("""{"id":1,"password":"***"}""");
    }

    [Fact]
    public void Legacy_AllowList_Behaviour_Documented()
    {
        const string json = """{"s":"x","n":1,"b":true,"z":null}""";
#pragma warning disable CS0618
        var legacy = JsonObserver.Obj(_ => { }, JsonObserverValuePolicies.LegacyAllowList);
#pragma warning restore CS0618
        var allowList = JsonObserver.Obj(_ => { }, JsonObserverValuePolicies.AllowList);
        var byDefault = JsonObserver.Obj(_ => { });

        legacy.Mask(json).Should().Be("""{"s":"#str#*****","n":"#number#*****","b":true,"z":null}""");
        allowList.Mask(json).Should().Be("""{"s":"***","n":"***","b":"***","z":null}""");
        byDefault.Mask(json).Should().Be(allowList.Mask(json));
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class SensitiveAttribute(MaskKind kind = MaskKind.Full) : Attribute
    {
        public MaskKind Kind { get; } = kind;
    }

    public sealed class Customer
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool Active { get; set; }

        [Sensitive]
        public string? Password { get; set; }

        [Sensitive(MaskKind.Last4)]
        public string? Card { get; set; }

        [Sensitive(MaskKind.Omit)]
        public int? Pin { get; set; }

        [Sensitive]
        [JsonPropertyName("e_mail")]
        public string? Email { get; set; }

        public List<Order>? Orders { get; set; }
        public string[]? Tags { get; set; }
        public Dictionary<string, Order>? ByCode { get; set; }
        public object? Extra { get; set; }
        public JsonElement Raw { get; set; }
        public Customer? Referrer { get; set; }

        [JsonIgnore]
        public string? Ignored { get; set; }
    }

    public sealed class Order
    {
        public string? Sku { get; set; }

        [Sensitive]
        public string? SecretCode { get; set; }
    }
}
