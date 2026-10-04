namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// The rules of one object or array, kept beside the delegate built from them so that a path can be explained.
/// </summary>
internal sealed record RuleSet<TContext>(bool IsArray, JsonObserverItem<TContext>[] Items, JsonObserverValueDelegate<TContext>? ValuePolicy);

/// <summary>
/// What a rule tests and what it does, for <see cref="JsonPathExplanation"/>.
/// </summary>
/// <param name="Match">The test, for example <c>Match("card", "number")</c>.</param>
/// <param name="Action">The action, for example <c>MaskAny("***")</c>.</param>
/// <param name="Outcome">What the action does to the value.</param>
/// <param name="Child">Rules for the matched object or array; <c>null</c> for a value rule or a custom container rule.</param>
internal sealed record RuleInfo<TContext>(string Match, string Action, JsonPathOutcome Outcome, RuleSet<TContext>? Child = null)
{
    public static RuleInfo<TContext> Unknown { get; } = new("rule", "custom rule", JsonPathOutcome.Custom);
}
