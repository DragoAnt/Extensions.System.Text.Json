using DragoAnt.System.Text.Json.Observer.Builders;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Explains a rule-based observer by walking its rules the way the masking pass does, with a path built from the
/// explained one.
/// </summary>
internal sealed class RuleExplainer<TContext>(RuleSet<TContext>? obj, RuleSet<TContext>? array) : PathExplainer
{
    protected override (PathOutcome Outcome, string Rule, string Action) Explain(
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
            return (PathOutcome.Invalid, "root", $"a root {root} makes the payload Invalid");
        }

        var path = PathOf(segments, propertyNameCaseInsensitive);
        try
        {
            var effective = set.ValuePolicy ?? JsonValuePolicy<TContext>.Default;
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
                var reads = last ? ReadsAt(items, depth, ref path, token) : [];
                foreach (var read in reads)
                {
                    steps.Add($"{at}: {read.Match} → {read.Action}");
                }

                if (item is not null)
                {
                    var info = item.Info;
                    chain.Add(info.Match);
                    if (last || info.Child is null)
                    {
                        var (outcome, action) = last ? Resolve(info, token) : (info.Outcome, $"{info.Action} on the whole {Container(token)}");
                        steps.Add($"{at}: {info.Match} → {action}");
                        return WithReads((outcome, string.Join(" > ", chain), action), chain.GetRange(0, chain.Count - 1), reads);
                    }

                    steps.Add($"{at}: {info.Match} → {info.Action}");
                    items = info.Child.Items;
                    depth = nextDepth;
                    effective = info.Child.ValuePolicy ?? effective;
                    continue;
                }

                if (!last)
                {
                    if (effective.Relative is { } relative)
                    {
                        var (relativeItem, _) = JsonObserverItem<TContext>.MatchPolicy(relative.Items, 0, ref path, token);
                        if (relativeItem is not null)
                        {
                            var info = relativeItem.Info;
                            steps.Add($"{at}: AnyDepth {info.Match} → {info.Action} on the whole {Container(token)}");
                            return (info.Outcome, $"AnyDepth {info.Match}", $"{info.Action} on the whole {Container(token)}");
                        }
                    }

                    steps.Add($"{at}: no rule; the {Container(token)} is descended with the same rules");
                    continue;
                }

                return WithReads(DefaultPolicy(effective, ref path, token, at, steps), chain, reads);
            }

            throw new InvalidOperationException("Unreachable: the last segment always returns.");
        }
        finally
        {
            path.Dispose();
        }
    }

    private static (PathOutcome, string, string) DefaultPolicy(
        JsonValuePolicy<TContext> policy,
        ref JsonWalk path,
        JsonTokenType token,
        string at,
        List<string> steps)
    {
        if (policy.Relative is { } relative)
        {
            var reads = ReadsAt(relative.Items, 0, ref path, token);
            foreach (var read in reads)
            {
                steps.Add($"{at}: AnyDepth {read.Match} → {read.Action}");
            }

            var (item, _) = JsonObserverItem<TContext>.MatchPolicy(relative.Items, 0, ref path, token);
            if (item is not null)
            {
                var (relativeOutcome, relativeAction) = Resolve(item.Info, token);
                steps.Add($"{at}: AnyDepth {item.Info.Match} → {relativeAction}");
                return WithReads((relativeOutcome, $"AnyDepth {item.Info.Match}", relativeAction), ["AnyDepth"], reads);
            }

            steps.Add($"{at}: no AnyDepth rule");
            return WithReads(DefaultPolicy(relative.Fallback, ref path, token, at, steps), ["AnyDepth"], reads);
        }

        var builtIn = policy.BuiltIn;
        var rule = builtIn is null ? policy.Name : $"default policy {builtIn}";
        var (outcome, action) = token is JsonTokenType.Null
            ? (PathOutcome.Unchanged, "keeps null")
            : builtIn?.Kind switch
            {
                ValuePolicyKind.AllowList => (PathOutcome.Masked, "writes \"***\""),
                ValuePolicyKind.BlockList => (PathOutcome.Unchanged, "writes the value as is"),
                ValuePolicyKind.NullList => (PathOutcome.Masked, "writes null"),
                ValuePolicyKind.Tagged => (PathOutcome.Masked, RuleText.Tag(builtIn.Tag)),
                _ => (PathOutcome.Custom, "custom default policy decides"),
            };
        if (token is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            (outcome, action) = (PathOutcome.Unchanged, $"the {Container(token)} is descended with the same rules");
        }

        steps.Add($"{at}: {rule} → {action}");
        return (outcome, rule, action);
    }

    private static List<RuleInfo<TContext>> ReadsAt(JsonObserverItem<TContext>[] items, int depth, ref JsonWalk path, JsonTokenType token)
    {
        List<RuleInfo<TContext>> reads = [];
        foreach (var item in items)
        {
            if (item.Reader is not null && item.Match(depth, ref path, token).success)
            {
                reads.Add(item.Info);
            }
        }

        return reads;
    }

    /// <summary>
    /// A value that read rules hand to the context is still written by the rule or policy that decided the result.
    /// </summary>
    private static (PathOutcome, string, string) WithReads(
        (PathOutcome Outcome, string Rule, string Action) written,
        List<string> chain,
        List<RuleInfo<TContext>> reads)
    {
        if (reads.Count == 0)
        {
            return written;
        }

        var readRules = string.Join(" + ", reads.Select(r => string.Join(" > ", chain.Append(r.Match))));
        var readActions = string.Join("; ", reads.Select(r => r.Action));
        var outcome = written.Outcome is PathOutcome.Unchanged ? PathOutcome.Read : written.Outcome;
        return (outcome, $"{readRules} + {written.Rule}", $"{readActions}; {written.Action}");
    }

    private static (PathOutcome Outcome, string Action) Resolve(RuleInfo<TContext> info, JsonTokenType token) =>
        token is JsonTokenType.Null && info.KeepsNull
            ? (PathOutcome.Unchanged, $"{info.Action} keeps null")
            : (info.Outcome, info.Action);

    private static string Container(JsonTokenType token) => token is JsonTokenType.StartArray ? "array" : "object";
}
