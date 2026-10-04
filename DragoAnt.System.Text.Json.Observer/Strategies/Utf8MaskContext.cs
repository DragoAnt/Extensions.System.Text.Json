namespace DragoAnt.System.Text.Json.Observer.Strategies;

/// <summary>
/// What a <see cref="Utf8MaskStrategy"/> knows about the value it masks. Valid only during the call it is passed to;
/// reading it allocates nothing.
/// </summary>
public readonly ref struct Utf8MaskContext
{
    private readonly PropertyPath _path;

    internal Utf8MaskContext(ReadOnlySpan<byte> value, JsonTokenType tokenType, MaskTag tag, JsonObserverOptions options, PropertyPath path)
    {
        Value = value;
        TokenType = tokenType;
        Tag = tag;
        Options = options;
        _path = path;
    }

    /// <summary>
    /// The unescaped text of a string, the literal of a number or boolean, or empty for <c>null</c>, an object or an array.
    /// </summary>
    public ReadOnlySpan<byte> Value { get; }

    /// <summary>
    /// JSON type of the value; <see cref="JsonTokenType.StartObject"/> or <see cref="JsonTokenType.StartArray"/> for a container.
    /// </summary>
    public JsonTokenType TokenType { get; }

    /// <summary>
    /// How the rule asks for the value to be masked.
    /// </summary>
    public MaskTag Tag { get; }

    /// <summary>
    /// Options of the current call.
    /// </summary>
    public JsonObserverOptions Options { get; }

    /// <summary>
    /// Path of the value from the root, array indices included.
    /// </summary>
    public PropertyPath Path => _path;

    /// <summary>
    /// Unescaped UTF-8 name of the property that holds the value; empty for an array item.
    /// </summary>
    public ReadOnlySpan<byte> PropertyName => _path.TryGetPropertyNameUtf8(_path.Length - 1, out var name) ? name : default;

    /// <summary>
    /// The value is an item of an array rather than the value of a property.
    /// </summary>
    public bool IsArrayItem => _path.IsArrayItem(_path.Length - 1);
}
