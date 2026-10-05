using System.Diagnostics.CodeAnalysis;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Everything a <see cref="JsonValueRule{TContext}"/> works with: the reader on the value, the writer, the context and
/// the path. Valid only during the call it is passed to; new members are added without breaking existing rules.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
public ref struct JsonValueContext<TContext>
{
    private Utf8JsonReader _reader;
    private JsonWalk _walk;
    private readonly ObserveRule<TContext>? _fallback;
    private readonly ValueRule<TContext> _defaultValue;
    private readonly int _depth;

    internal JsonValueContext(
        scoped ref Utf8JsonReader reader,
        JsonWriter writer,
        TContext context,
        int depth,
        scoped ref JsonWalk walk,
        ValueRule<TContext> defaultValue,
        ObserveRule<TContext>? fallback)
    {
        _reader = reader;
        _walk = walk;
        Writer = writer;
        Context = context;
        _depth = depth;
        _defaultValue = defaultValue;
        _fallback = fallback;
    }

    /// <summary>
    /// The reader, positioned on the value: a scalar, or the start of an object or array. A rule that moves past a
    /// container must leave it on the container's end.
    /// </summary>
    [UnscopedRef]
    public ref Utf8JsonReader Reader => ref _reader;

    /// <summary>
    /// JSON type of the value the reader stands on.
    /// </summary>
    public readonly JsonTokenType TokenType => _reader.TokenType;

    /// <summary>
    /// Receives exactly one value.
    /// </summary>
    public JsonWriter Writer { get; }

    /// <summary>
    /// The context of the call, where read rules put extracted values.
    /// </summary>
    public TContext Context { get; }

    /// <summary>
    /// Path of the value from the root, array items included.
    /// </summary>
    public readonly DataPath Path => _walk.Path;

    /// <summary>
    /// Options of the call.
    /// </summary>
    public readonly JsonObserverOptions Options => _walk.Options;

    /// <summary>
    /// Writes the value the way the enclosing default policy would: a string, number, boolean or <c>null</c> through the
    /// policy, an object or array descended with it. Use it for the values a custom rule does not handle itself.
    /// </summary>
    public void WriteDefault()
    {
        if (_fallback is not null)
        {
            _fallback(ref _reader, Writer, Context, _depth, ref _walk, _defaultValue);
            return;
        }

        if (_reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            BuiltInPolicies<TContext>.MaskContainer(ref _reader, Writer, ref _walk);
            return;
        }

        _defaultValue(ref _reader, Writer, Context, ref _walk);
    }

    internal readonly void CopyBack(ref Utf8JsonReader reader, ref JsonWalk walk)
    {
        reader = _reader;
        walk = _walk;
    }
}
