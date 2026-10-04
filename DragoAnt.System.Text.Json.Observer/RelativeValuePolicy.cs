namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// A relative value policy; kept as the delegate target so that its whole-value rules can be offered containers too.
/// </summary>
internal sealed class RelativeValuePolicy<TContext>(
    JsonObserverDelegate<TContext> policy,
    JsonObserverItem<TContext>[] items,
    JsonObserverValueDelegate<TContext> defaultValuePolicy)
{
    public JsonObserverItem<TContext>[] Items => items;

    public JsonObserverValueDelegate<TContext> DefaultValuePolicy => defaultValuePolicy;

    public void Invoke(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref PropertyPath propPath)
        => policy(ref reader, writer, context, 0, ref propPath, defaultValuePolicy);

    public bool TryApplyContainer(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref PropertyPath propPath)
    {
        var (match, depth) = JsonObserverItem<TContext>.MatchPolicy(items, 0, ref propPath, reader.TokenType);
        if (match is null)
        {
            return false;
        }

        match.Apply(ref reader, writer, context, depth, ref propPath, defaultValuePolicy);
        return true;
    }
}
