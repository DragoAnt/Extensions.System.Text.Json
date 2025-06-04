namespace DragoAnt.System.Text.Json.Observer;

public delegate void JsonObserverDelegate<TContext>(
    ref Utf8JsonReader reader,
    JsonWriter writer,
    TContext context,
    int depth,
    ref PropertyPath propPath,
    JsonObserverValueDelegate<TContext> defaultValue);


public delegate void JsonObserverValueDelegate<in TContext>(
    ref Utf8JsonReader reader,
    JsonWriter writer,
    TContext context,
    ref PropertyPath propPath);

public delegate (bool success, int depth) JsonPropertyMatchDelegate(
    int depth,
    ref PropertyPath propPath,
    JsonTokenType tokenType);

public delegate (bool success, int depth) JsonPropertyPathMatchDelegate(int depth, ref PropertyPath propPath);