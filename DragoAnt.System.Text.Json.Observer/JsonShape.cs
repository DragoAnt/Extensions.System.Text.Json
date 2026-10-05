using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Kind of a <see cref="JsonShape"/> node.
/// </summary>
public enum JsonShapeKind
{
    /// <summary>
    /// A string, number, boolean or <c>null</c> written as is.
    /// </summary>
    Scalar,

    /// <summary>
    /// An object with known properties.
    /// </summary>
    Object,

    /// <summary>
    /// An array whose items have one shape.
    /// </summary>
    Array,

    /// <summary>
    /// An object used as a dictionary: every property name is shown, every value has one shape.
    /// </summary>
    Map,

    /// <summary>
    /// A sensitive value, masked whole with a <see cref="MaskTag"/>.
    /// </summary>
    Masked,

    /// <summary>
    /// A value of unknown structure, masked whole.
    /// </summary>
    Opaque,
}

/// <summary>
/// Expected structure of a JSON payload. Anything the shape does not describe is masked, so a field a remote side adds
/// later is never logged in clear.
/// </summary>
public sealed class JsonShape
{
    private static readonly Type[] OpaqueTypes = [typeof(object), typeof(JsonElement), typeof(JsonDocument), typeof(JsonNode)];

    private readonly List<JsonShapeProperty>? _members;
    private static readonly object SealSync = new();
    private (byte[]? Ascii, JsonShapeProperty Property)[] _lookup = [];
    private (byte[]? Ascii, JsonShapeProperty Property)[] _exactLookup = [];
    private volatile bool _sealed;

    private JsonShape(JsonShapeKind kind, MaskTag tag = default, JsonShape? item = null, JsonTypeInfo? typeInfo = null)
    {
        Kind = kind;
        Tag = tag;
        Item = item;
        TypeInfo = typeInfo;
        _members = kind == JsonShapeKind.Object ? [] : null;
    }

    /// <summary>
    /// Kind of the node.
    /// </summary>
    public JsonShapeKind Kind { get; }

    /// <summary>
    /// Mask of a <see cref="JsonShapeKind.Masked"/> node.
    /// </summary>
    public MaskTag Tag { get; }

    /// <summary>
    /// Item shape of an <see cref="JsonShapeKind.Array"/>, value shape of a <see cref="JsonShapeKind.Map"/>.
    /// </summary>
    public JsonShape? Item { get; private set; }

    /// <summary>
    /// Known properties of an <see cref="JsonShapeKind.Object"/>, in the order they were added.
    /// </summary>
    public IReadOnlyList<JsonShapeProperty> Members => (IReadOnlyList<JsonShapeProperty>?)_members?.AsReadOnly() ?? [];

    /// <summary>
    /// The System.Text.Json metadata the node was built from; <c>null</c> for a hand-built node and for the shared
    /// <see cref="Scalar"/> and <see cref="Opaque"/> nodes.
    /// </summary>
    public JsonTypeInfo? TypeInfo { get; }

    /// <summary>
    /// CLR type of the node, from <see cref="TypeInfo"/>.
    /// </summary>
    public Type? ClrType => TypeInfo?.Type;

    /// <summary>
    /// Data an integration attaches to the node.
    /// </summary>
    public JsonShapeAnnotations Annotations { get; } = new();

    /// <summary>
    /// A value written as is.
    /// </summary>
    public static JsonShape Scalar { get; } = new(JsonShapeKind.Scalar);

    /// <summary>
    /// A value of unknown structure, masked whole.
    /// </summary>
    public static JsonShape Opaque { get; } = new(JsonShapeKind.Opaque);

    internal static JsonShape UnknownMaskWhole { get; } = new(JsonShapeKind.Opaque);

    internal static JsonShape UnknownDescend { get; } = new(JsonShapeKind.Opaque);

    internal static JsonShape UnknownPassThrough { get; } = new(JsonShapeKind.Opaque);

    /// <summary>
    /// A sensitive value masked whole with <paramref name="tag"/>.
    /// </summary>
    public static JsonShape Masked(MaskTag tag) => new(JsonShapeKind.Masked, tag);

    /// <summary>
    /// An array whose items have the shape <paramref name="item"/>.
    /// </summary>
    public static JsonShape Array(JsonShape item) => new(JsonShapeKind.Array, item: item);

    /// <summary>
    /// A dictionary object: property names are shown, values have the shape <paramref name="value"/>.
    /// </summary>
    public static JsonShape Map(JsonShape value) => new(JsonShapeKind.Map, item: value);

    /// <summary>
    /// An object with the given properties; add more with <see cref="Add(string, JsonShape)"/>, which also allows cycles.
    /// </summary>
    public static JsonShape Object(params (string Name, JsonShape Shape)[] members)
    {
        var shape = new JsonShape(JsonShapeKind.Object);
        foreach (var (name, member) in members)
        {
            shape.Add(name, member);
        }

        return shape;
    }

    /// <summary>
    /// Adds a known property to an object shape. Names match case-insensitively; when two names collide the masked one wins.
    /// </summary>
    /// <exception cref="InvalidOperationException">The shape is not an object, or an observer was already built from it.</exception>
    public JsonShape Add(string name, JsonShape shape) => Add(new JsonShapeProperty(name, shape));

    /// <summary>
    /// Adds a known property, with its annotations, to an object shape. When two names collide the masked one wins.
    /// </summary>
    /// <exception cref="InvalidOperationException">The shape is not an object, or an observer was already built from it.</exception>
    public JsonShape Add(JsonShapeProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (_members is null || _sealed)
        {
            throw new InvalidOperationException(_members is null
                ? "Only an object shape has properties."
                : "The shape is in use by an observer and can no longer change.");
        }

        _members.Add(property);
        return this;
    }

    /// <summary>
    /// Builds a shape from System.Text.Json metadata: property names after the naming policy and
    /// <c>JsonPropertyNameAttribute</c>, collections, dictionaries and recursive types.
    /// </summary>
    /// <param name="typeInfo">Metadata of the root type.</param>
    /// <param name="classify">Mask for a sensitive property, or <c>null</c> for one shown as is.</param>
    /// <param name="annotate">
    /// Called once per property with its <see cref="JsonShapeProperty"/>, for example to attach annotations; a type
    /// reached twice, or recursively, is built and annotated once.
    /// </param>
    /// <remarks>
    /// Every node and property carries its metadata: <see cref="TypeInfo"/>, <see cref="JsonShapeProperty.PropertyInfo"/>,
    /// the CLR member, nullability and attributes. On .NET 8, metadata from a source-generated <c>JsonSerializerContext</c>
    /// has no <see cref="JsonPropertyInfo.AttributeProvider"/>, so a <paramref name="classify"/> that reads attributes finds
    /// none and shows every property; classify by name there, or use reflection-based metadata.
    /// </remarks>
    public static JsonShape FromTypeInfo(JsonTypeInfo typeInfo, Func<JsonPropertyInfo, MaskTag?> classify, Action<JsonShapeProperty>? annotate = null)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(classify);
        return new Builder(classify, annotate).Build(typeInfo);
    }

    /// <summary>
    /// Finds a known property of an object shape by its unescaped UTF-8 JSON name, as the observer does. The first lookup
    /// freezes the shape like building an observer from it.
    /// </summary>
    /// <param name="utf8Name">Unescaped UTF-8 name.</param>
    /// <param name="propertyNameCaseInsensitive">Compare names ignoring case, as by default.</param>
    /// <returns>The property, or <c>null</c> when the shape does not know it or is not an object.</returns>
    internal JsonShapeProperty? FindMember(ReadOnlySpan<byte> utf8Name, bool propertyNameCaseInsensitive = true)
    {
        if (!_sealed)
        {
            Freeze();
        }

        return FindProperty(utf8Name, propertyNameCaseInsensitive);
    }

    internal JsonShape? Find(ReadOnlySpan<byte> utf8Name, bool ignoreCase) => FindProperty(utf8Name, ignoreCase)?.Shape;

    private JsonShapeProperty? FindProperty(ReadOnlySpan<byte> utf8Name, bool ignoreCase)
    {
        var lookup = ignoreCase ? _lookup : _exactLookup;
        if (Ascii.IsValid(utf8Name))
        {
            foreach (var (ascii, property) in lookup)
            {
                if (ascii is not null && ascii.Length == utf8Name.Length &&
                    (ignoreCase ? Ascii.EqualsIgnoreCase(ascii, utf8Name) : utf8Name.SequenceEqual(ascii)))
                {
                    return property;
                }
            }

            return null;
        }

        var name = Encoding.UTF8.GetString(utf8Name);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var (_, property) in lookup)
        {
            if (string.Equals(property.Name, name, comparison))
            {
                return property;
            }
        }

        return null;
    }

    internal void Freeze()
    {
        lock (SealSync)
        {
            Seal([]);
        }
    }

    private void Seal(HashSet<JsonShape> visited)
    {
        if (!visited.Add(this))
        {
            return;
        }

        if (_members is not null)
        {
            _lookup = Merge(_members, StringComparison.OrdinalIgnoreCase);
            _exactLookup = Merge(_members, StringComparison.Ordinal);
        }

        _sealed = true;
        foreach (var property in _members ?? [])
        {
            property.Shape.Seal(visited);
        }

        Item?.Seal(visited);
    }

    private static (byte[]? Ascii, JsonShapeProperty Property)[] Merge(List<JsonShapeProperty> members, StringComparison comparison)
    {
        var merged = new List<JsonShapeProperty>();
        foreach (var property in members)
        {
            var index = merged.FindIndex(m => string.Equals(m.Name, property.Name, comparison));
            if (index < 0)
            {
                merged.Add(property);
            }
            else if (property.Shape.Kind is JsonShapeKind.Masked or JsonShapeKind.Opaque)
            {
                merged[index] = property;
            }
        }

        return merged.Select(m => (Ascii.IsValid(m.Name) ? Encoding.ASCII.GetBytes(m.Name) : null, m)).ToArray();
    }

    private sealed class Builder(Func<JsonPropertyInfo, MaskTag?> classify, Action<JsonShapeProperty>? annotate)
    {
        private readonly Dictionary<Type, JsonShape> _built = [];
#if !NET9_0_OR_GREATER
        private readonly NullabilityInfoContext _nullability = new();
#endif

        public JsonShape Build(JsonTypeInfo typeInfo)
        {
            var type = typeInfo.Type;
            if (_built.TryGetValue(type, out var existing))
            {
                return existing;
            }

            if (OpaqueTypes.Any(t => t.IsAssignableFrom(type) && (t != typeof(object) || type == typeof(object))))
            {
                return Opaque;
            }

            switch (typeInfo.Kind)
            {
                case JsonTypeInfoKind.Object:
                {
                    var shape = new JsonShape(JsonShapeKind.Object, typeInfo: typeInfo);
                    _built[type] = shape;
                    foreach (var property in typeInfo.Properties.Where(p => !p.IsExtensionData))
                    {
                        var tag = classify(property);
                        var member = new JsonShapeProperty(
                            property.Name,
                            tag is { } mask ? Masked(mask) : Build(typeInfo.Options.GetTypeInfo(property.PropertyType)),
                            property,
                            Nullability(property));
                        shape.Add(member);
                        annotate?.Invoke(member);
                    }

                    return shape;
                }
                case JsonTypeInfoKind.Enumerable:
                case JsonTypeInfoKind.Dictionary:
                {
                    var shape = new JsonShape(typeInfo.Kind == JsonTypeInfoKind.Enumerable ? JsonShapeKind.Array : JsonShapeKind.Map, typeInfo: typeInfo);
                    _built[type] = shape;
                    shape.Item = ElementTypeOf(typeInfo) is { } elementType
                        ? Build(typeInfo.Options.GetTypeInfo(elementType))
                        : Opaque;
                    return shape;
                }
                default:
                {
                    var shape = new JsonShape(JsonShapeKind.Scalar, typeInfo: typeInfo);
                    _built[type] = shape;
                    return shape;
                }
            }
        }

#if NET9_0_OR_GREATER
        private static bool? Nullability(JsonPropertyInfo property) => JsonShapeProperty.NullabilityOf(property, null);
#else
        private bool? Nullability(JsonPropertyInfo property) => JsonShapeProperty.NullabilityOf(property, _nullability);
#endif
    }

    private static Type? ElementTypeOf(JsonTypeInfo typeInfo)
    {
#if NET9_0_OR_GREATER
        if (typeInfo.ElementType is { } elementType)
        {
            return elementType;
        }
#endif
        var type = typeInfo.Type;
        var dictionary = typeInfo.Kind == JsonTypeInfoKind.Dictionary;
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        var interfaces = type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces();
        foreach (var candidate in interfaces.Where(i => i.IsGenericType))
        {
            var definition = candidate.GetGenericTypeDefinition();
            if (dictionary && (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)))
            {
                return candidate.GetGenericArguments()[1];
            }

            if (!dictionary && definition == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
