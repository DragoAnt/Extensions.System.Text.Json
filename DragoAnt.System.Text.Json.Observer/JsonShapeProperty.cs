using System.Reflection;
using System.Text.Json.Serialization.Metadata;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// A known property of an object <see cref="JsonShape"/>: its JSON name, the shape of its value and, when the shape was
/// built from System.Text.Json metadata, what that metadata says about the CLR member.
/// </summary>
/// <remarks>
/// On .NET 8, metadata from a source-generated <c>JsonSerializerContext</c> has no attribute provider, so
/// <see cref="Member"/>, <see cref="AttributeProvider"/> and <see cref="GetCustomAttributes{T}"/> are empty and
/// <see cref="IsNullable"/> is <c>null</c> for reference types; reflection-based metadata and .NET 9 or later carry them.
/// </remarks>
public sealed class JsonShapeProperty
{
    /// <summary>
    /// Creates a property for a hand-built shape; it carries no CLR metadata.
    /// </summary>
    /// <param name="name">JSON name of the property.</param>
    /// <param name="shape">Shape of its value.</param>
    public JsonShapeProperty(string name, JsonShape shape)
        : this(name, shape, null, null)
    {
    }

    internal JsonShapeProperty(string name, JsonShape shape, JsonPropertyInfo? propertyInfo, bool? isNullable)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(shape);
        Name = name;
        Shape = shape;
        PropertyInfo = propertyInfo;
        IsNullable = isNullable;
    }

    /// <summary>
    /// JSON name of the property, after the naming policy and <c>JsonPropertyNameAttribute</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Shape of the property's value.
    /// </summary>
    public JsonShape Shape { get; }

    /// <summary>
    /// The System.Text.Json metadata the property was built from; <c>null</c> for a hand-built shape.
    /// </summary>
    public JsonPropertyInfo? PropertyInfo { get; }

    /// <summary>
    /// CLR type of the property.
    /// </summary>
    public Type? PropertyType => PropertyInfo?.PropertyType;

    /// <summary>
    /// The CLR type that declares the property.
    /// </summary>
    public Type? DeclaringType =>
#if NET9_0_OR_GREATER
        PropertyInfo?.DeclaringType;
#else
        Member?.DeclaringType;
#endif

    /// <summary>
    /// Attributes of the CLR member, see the remarks for .NET 8.
    /// </summary>
    public ICustomAttributeProvider? AttributeProvider => PropertyInfo?.AttributeProvider;

    /// <summary>
    /// The CLR property or field, see the remarks for .NET 8.
    /// </summary>
    public MemberInfo? Member => AttributeProvider as MemberInfo;

    /// <summary>
    /// The property must be present when deserializing (<c>required</c> or <c>JsonRequiredAttribute</c>).
    /// </summary>
    public bool IsRequired => PropertyInfo?.IsRequired ?? false;

    /// <summary>
    /// Whether the value may be <c>null</c> when deserializing: from <see cref="Nullable{T}"/> for value types and from the
    /// nullable annotations for reference types; <c>null</c> when unknown, see the remarks for .NET 8.
    /// </summary>
    public bool? IsNullable { get; }

    /// <summary>
    /// Data an integration attaches to the property.
    /// </summary>
    public JsonShapeAnnotations Annotations { get; } = new();

    /// <summary>
    /// Custom attributes of type <typeparamref name="T"/> on the CLR member; empty when unknown.
    /// </summary>
    /// <param name="inherit">Search the member's inheritance chain.</param>
    /// <typeparam name="T">Attribute type.</typeparam>
    public IEnumerable<T> GetCustomAttributes<T>(bool inherit = true)
        where T : Attribute =>
        AttributeProvider?.GetCustomAttributes(typeof(T), inherit).OfType<T>() ?? [];

    /// <summary>
    /// Lets a property be deconstructed like a <c>(Name, Shape)</c> tuple.
    /// </summary>
    /// <param name="name">JSON name.</param>
    /// <param name="shape">Shape of the value.</param>
    public void Deconstruct(out string name, out JsonShape shape)
    {
        name = Name;
        shape = Shape;
    }

    internal static bool? NullabilityOf(JsonPropertyInfo property, NullabilityInfoContext? context)
    {
        var type = property.PropertyType;
        if (type.IsValueType)
        {
            return Nullable.GetUnderlyingType(type) is not null;
        }

#if NET9_0_OR_GREATER
        return property.IsSetNullable;
#else
        try
        {
            var info = property.AttributeProvider switch
            {
                PropertyInfo p when context is not null => context.Create(p),
                FieldInfo f when context is not null => context.Create(f),
                _ => null,
            };
            return info?.WriteState switch
            {
                NullabilityState.Nullable => true,
                NullabilityState.NotNull => false,
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
#endif
    }
}
