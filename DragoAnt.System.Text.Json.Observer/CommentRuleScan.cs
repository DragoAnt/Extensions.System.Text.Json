namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Finds whether an observer's rules decide comments anywhere, so that a call can skip comments when none would be written.
/// </summary>
internal static class CommentRuleScan
{
    public static bool Any<TContext>(RuleSet<TContext>? set) => set is not null && Any(set.Items, set.ValuePolicy, 0);

    private static bool Any<TContext>(JsonObserverItem<TContext>[] items, JsonValuePolicy<TContext>? policy, int depth)
    {
        if (depth > 64)
        {
            return false;
        }

        foreach (var item in items)
        {
            if (item.CommentRule is not null || (item.Info.Child is { } child && Any(child.Items, child.ValuePolicy, depth + 1)))
            {
                return true;
            }
        }

        return policy?.Relative is { } relative && Any(relative.Items, relative.Fallback, depth + 1);
    }
}
