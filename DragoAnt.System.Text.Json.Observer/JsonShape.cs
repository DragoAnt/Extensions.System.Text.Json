using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DragoAnt.System.Text.Json.Observer.Strategies;

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

    private readonly List<(string Name, JsonShape Shape)>? _members;
    private (byte[]? Ascii, string Name, JsonShape Shape)[] _lookup = [];
    private bool _sealed;

    private JsonShape(JsonShapeKind kind, MaskTag tag = default, JsonShape? item = null)
    {
        Kind = kind;
        Tag = tag;
        Item = item;
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
    /// Known properties of an <see cref="JsonShapeKind.Object"/>.
    /// </summary>
    public IReadOnlyList<(string Name, JsonShape Shape)> Members => _members ?? [];

    /// <summary>
    /// A value written as is.
    /// </summary>
    public static JsonShape Scalar { get; } = new(JsonShapeKind.Scalar);

    /// <summary>
    /// A value of unknown structure, masked whole.
    /// </summary>
    public static JsonShape Opaque { get; } = new(JsonShapeKind.Opaque);

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
    /// An object with the given properties; add more with <see cref="Add"/>, which also allows cycles.
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
    public JsonShape Add(string name, JsonShape shape)
    {
        if (_members is null || _sealed)
        {
            throw new InvalidOperationException(_members is null
                ? "Only an object shape has properties."
                : "The shape is in use by an observer and can no longer change.");
        }

        _members.Add((name, shape));
        return this;
    }

    /// <summary>
    /// Builds a shape from System.Text.Json metadata: property names after the naming policy and
    /// <c>JsonPropertyNameAttribute</c>, collections, dictionaries and recursive types.
    /// </summary>
    /// <param name="typeInfo">Metadata of the root type.</param>
    /// <param name="classify">Mask for a sensitive property, or <c>null</c> for one shown as is.</param>
    public static JsonShape FromTypeInfo(JsonTypeInfo typeInfo, Func<JsonPropertyInfo, MaskTag?> classify)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(classify);
        return Build(typeInfo, classify, []);
    }

    internal JsonShape? Find(ReadOnlySpan<byte> utf8Name)
    {
        if (Ascii.IsValid(utf8Name))
        {
            foreach (var (ascii, _, shape) in _lookup)
            {
                if (ascii is not null && ascii.Length == utf8Name.Length && Ascii.EqualsIgnoreCase(ascii, utf8Name))
                {
                    return shape;
                }
            }

            return null;
        }

        var name = Encoding.UTF8.GetString(utf8Name);
        foreach (var (_, candidate, shape) in _lookup)
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return shape;
            }
        }

        return null;
    }

    internal void Seal(HashSet<JsonShape> visited)
    {
        if (!visited.Add(this))
        {
            return;
        }

        _sealed = true;
        if (_members is not null)
        {
            var merged = new List<(string Name, JsonShape Shape)>();
            foreach (var (name, shape) in _members)
            {
                var index = merged.FindIndex(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    merged.Add((name, shape));
                }
                else if (shape.Kind is JsonShapeKind.Masked or JsonShapeKind.Opaque)
                {
                    merged[index] = (name, shape);
                }
            }

            _lookup = merged
                .Select(m => (Ascii.IsValid(m.Name) ? Encoding.ASCII.GetBytes(m.Name) : null, m.Name, m.Shape))
                .ToArray();
            foreach (var (_, shape) in merged)
            {
                shape.Seal(visited);
            }
        }

        Item?.Seal(visited);
    }

    private static JsonShape Build(JsonTypeInfo typeInfo, Func<JsonPropertyInfo, MaskTag?> classify, Dictionary<Type, JsonShape> built)
    {
        var type = typeInfo.Type;
        if (built.TryGetValue(type, out var existing))
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
                var shape = new JsonShape(JsonShapeKind.Object);
                built[type] = shape;
                foreach (var property in typeInfo.Properties.Where(p => !p.IsExtensionData))
                {
                    var tag = classify(property);
                    shape.Add(property.Name, tag is { } mask
                        ? Masked(mask)
                        : Build(typeInfo.Options.GetTypeInfo(property.PropertyType), classify, built));
                }

                return shape;
            }
            case JsonTypeInfoKind.Enumerable:
            case JsonTypeInfoKind.Dictionary:
            {
                var shape = new JsonShape(typeInfo.Kind == JsonTypeInfoKind.Enumerable ? JsonShapeKind.Array : JsonShapeKind.Map);
                built[type] = shape;
                shape.Item = ElementTypeOf(type, typeInfo.Kind == JsonTypeInfoKind.Dictionary) is { } elementType
                    ? Build(typeInfo.Options.GetTypeInfo(elementType), classify, built)
                    : Opaque;
                return shape;
            }
            default:
                return Scalar;
        }
    }

    private static Type? ElementTypeOf(Type type, bool dictionary)
    {
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
