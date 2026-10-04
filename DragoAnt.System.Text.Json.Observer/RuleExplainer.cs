namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Explains a rule-based observer by walking its rules the way the masking pass does, with a path built from the
/// explained one.
/// </summary>
internal sealed class RuleExplainer<TContext>(RuleSet<TContext>? obj, RuleSet<TContext>? array) : PathExplainer
{
    protected override (JsonPathOutcome Outcome, string Rule, string Action) Explain(
        IReadOnlyList<PathSegment> segments,
        JsonTokenType valueKind,
        bool propertyNameCaseInsensitive,
        List<string> steps)
    {
        var set = segments[0].IsIndex ? array : obj;
        if (set is null)
        {
            var root = segments[0].IsIndex ? "array" : "object";
            steps.Add($"$: the observer does not accept a root {root}");
            return (JsonPathOutcome.Invalid, "root", $"a root {root} makes the payload Invalid");
        }

        var path = PathOf(segments, propertyNameCaseInsensitive);
        try
        {
            var effective = set.ValuePolicy ?? JsonObserverValuePolicies<TContext>.Default;
            var items = set.Items;
            var depth = 0;
            var chain = new List<string>();
            for (var i = 0; i < segments.Count; i++)
            {
                Push(ref path, segments[i]);
                var at = Format(segments, i + 1);
                var token = TokenAt(segments, i, valueKind);
                var last = i == segments.Count - 1;
                var (item, nextDepth) = JsonObserverItem<TContext>.MatchPolicy(items, depth, ref path, token);
                if (item is not null)
                {
                    var info = item.Info;
                    chain.Add(info.Match);
                    if (last || info.Child is null)
                    {
                        var (outcome, action) = last ? Resolve(info, token) : (info.Outcome, $"{info.Action} on the whole {Container(token)}");
                        steps.Add($"{at}: {info.Match} → {action}");
                        return (outcome, string.Join(" > ", chain), action);
                    }

                    steps.Add($"{at}: {info.Match} → {info.Action}");
                    items = info.Child.Items;
                    depth = nextDepth;
                    effective = info.Child.ValuePolicy ?? effective;
                    continue;
                }

                if (!last)
                {
                    if (effective.Target is RelativeValuePolicy<TContext> relative)
                    {
                        var (relativeItem, _) = JsonObserverItem<TContext>.MatchPolicy(relative.Items, 0, ref path, token);
                        if (relativeItem is not null)
                        {
                            var info = relativeItem.Info;
                            steps.Add($"{at}: relative {info.Match} → {info.Action} on the whole {Container(token)}");
                            return (info.Outcome, $"relative {info.Match}", $"{info.Action} on the whole {Container(token)}");
                        }
                    }

                    steps.Add($"{at}: no rule; the {Container(token)} is descended with the same rules");
                    continue;
                }

                return DefaultPolicy(effective, ref path, token, at, steps);
            }

            throw new InvalidOperationException("Unreachable: the last segment always returns.");
        }
        finally
        {
            path.Dispose();
        }
    }

    private static (JsonPathOutcome, string, string) DefaultPolicy(
        JsonObserverValueDelegate<TContext> policy,
        ref PropertyPath path,
        JsonTokenType token,
        string at,
        List<string> steps)
    {
        if (policy.Target is RelativeValuePolicy<TContext> relative)
        {
            var (item, _) = JsonObserverItem<TContext>.MatchPolicy(relative.Items, 0, ref path, token);
            if (item is not null)
            {
                var (relativeOutcome, relativeAction) = Resolve(item.Info, token);
                steps.Add($"{at}: relative {item.Info.Match} → {relativeAction}");
                return (relativeOutcome, $"relative {item.Info.Match}", relativeAction);
            }

            steps.Add($"{at}: no relative rule");
            return DefaultPolicy(relative.DefaultValuePolicy, ref path, token, at, steps);
        }

        var name = KnownPolicyName(policy);
        var rule = name is null ? "custom default policy" : $"default policy {name}";
        var (outcome, action) = token is JsonTokenType.Null
            ? (JsonPathOutcome.Unchanged, "keeps null")
            : name switch
            {
                nameof(JsonObserverValuePolicies<TContext>.AllowList) => (JsonPathOutcome.Masked, "writes \"***\""),
                nameof(JsonObserverValuePolicies<TContext>.BlockList) => (JsonPathOutcome.Unchanged, "writes the value as is"),
                nameof(JsonObserverValuePolicies<TContext>.NullList) => (JsonPathOutcome.Masked, "writes null"),
                "LegacyAllowList" when token is JsonTokenType.True or JsonTokenType.False => (JsonPathOutcome.Unchanged, "writes the boolean as is"),
                "LegacyAllowList" => (JsonPathOutcome.Masked, token is JsonTokenType.String ? "writes \"#str#*****\"" : "writes \"#number#*****\""),
                _ => (JsonPathOutcome.Custom, "custom default policy decides"),
            };
        if (token is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            (outcome, action) = (JsonPathOutcome.Unchanged, $"the {Container(token)} is descended with the same rules");
        }

        steps.Add($"{at}: {rule} → {action}");
        return (outcome, rule, action);
    }

    private static string? KnownPolicyName(JsonObserverValueDelegate<TContext> policy)
    {
        var declaring = policy.Method.DeclaringType;
        return declaring is { IsGenericType: true } && declaring.GetGenericTypeDefinition() == typeof(JsonObserverValuePolicies<>)
            ? policy.Method.Name
            : null;
    }

    private static (JsonPathOutcome Outcome, string Action) Resolve(RuleInfo<TContext> info, JsonTokenType token) =>
        token is JsonTokenType.Null && info.Action.StartsWith("MaskAny(", StringComparison.Ordinal)
            ? (JsonPathOutcome.Unchanged, $"{info.Action} keeps null")
            : (info.Outcome, info.Action);

    private static string Container(JsonTokenType token) => token is JsonTokenType.StartArray ? "array" : "object";
}
