namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// The engine's rule: writes the value the reader stands on, passing <paramref name="depth"/> and the default policy on to nested rules.
/// </summary>
internal delegate void ObserveRule<TContext>(
    ref Utf8JsonReader reader,
    JsonWriter writer,
    TContext context,
    int depth,
    ref JsonWalk walk,
    ValueRule<TContext> defaultValue);

/// <summary>
/// The engine's policy for one string, number, boolean or <c>null</c> value.
/// </summary>
internal delegate void ValueRule<in TContext>(
    ref Utf8JsonReader reader,
    JsonWriter writer,
    TContext context,
    ref JsonWalk walk);

internal delegate (bool success, int depth) JsonPropertyMatchDelegate(
    int depth,
    ref JsonWalk walk,
    JsonTokenType tokenType);

internal delegate (bool success, int depth) JsonPropertyPathMatchDelegate(int depth, ref JsonWalk walk);

/// <summary>
/// Custom rule for one JSON value: reads the token <see cref="JsonValueContext{TContext}.Reader"/> stands on and
/// writes exactly one value to <see cref="JsonValueContext{TContext}.Writer"/>.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
/// <param name="context">The reader, the writer, the context, the path and the options of the call.</param>
public delegate void JsonValueRule<TContext>(ref JsonValueContext<TContext> context);
