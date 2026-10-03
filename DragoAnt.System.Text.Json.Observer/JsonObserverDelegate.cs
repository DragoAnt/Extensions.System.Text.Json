namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Custom rule for one JSON value: reads the token the reader stands on and writes its replacement.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
/// <param name="reader">Positioned on the value: a scalar, or the start of an object or array.</param>
/// <param name="writer">Receives exactly one value.</param>
/// <param name="context">Context of the call.</param>
/// <param name="depth">Nesting level of the rule that matched; pass it on unchanged to nested rules.</param>
/// <param name="propPath">Path of the value; a rule that moves past a container must leave the reader on its end.</param>
/// <param name="defaultValue">Policy for values no rule matches.</param>
public delegate void JsonObserverDelegate<TContext>(
    ref Utf8JsonReader reader,
    JsonWriter writer,
    TContext context,
    int depth,
    ref PropertyPath propPath,
    JsonObserverValueDelegate<TContext> defaultValue);

/// <summary>
/// Policy for one string, number, boolean or <c>null</c> value, such as <see cref="JsonObserverValuePolicies{TContext}.AllowList"/>.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
/// <param name="reader">Positioned on the value.</param>
/// <param name="writer">Receives exactly one value.</param>
/// <param name="context">Context of the call.</param>
/// <param name="propPath">Path of the value.</param>
public delegate void JsonObserverValueDelegate<in TContext>(
    ref Utf8JsonReader reader,
    JsonWriter writer,
    TContext context,
    ref PropertyPath propPath);

internal delegate (bool success, int depth) JsonPropertyMatchDelegate(
    int depth,
    ref PropertyPath propPath,
    JsonTokenType tokenType);

internal delegate (bool success, int depth) JsonPropertyPathMatchDelegate(int depth, ref PropertyPath propPath);
