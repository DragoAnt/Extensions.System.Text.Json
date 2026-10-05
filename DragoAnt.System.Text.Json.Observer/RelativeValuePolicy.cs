namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// An any-depth value policy; kept as the delegate target so that its whole-value rules can be offered containers too.
/// </summary>
internal sealed class RelativeValuePolicy<TContext>(
    ObserveRule<TContext> policy,
    JsonObserverItem<TContext>[] items,
    JsonValuePolicy<TContext> fallback)
{
    public JsonObserverItem<TContext>[] Items => items;

    public JsonValuePolicy<TContext> Fallback => fallback;

    public void Invoke(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref JsonWalk walk)
        => policy(ref reader, writer, context, 0, ref walk, fallback.Rule);

    public bool TryApplyContainer(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref JsonWalk walk)
    {
        var (match, depth) = JsonObserverItem<TContext>.MatchPolicy(items, 0, ref walk, reader.TokenType);
        if (match is null)
        {
            return false;
        }

        match.Apply(ref reader, writer, context, depth, ref walk, fallback.Rule);
        return true;
    }
}
