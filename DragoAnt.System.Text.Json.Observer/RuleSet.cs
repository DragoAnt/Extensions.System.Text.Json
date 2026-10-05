namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// The rules of one object or array, kept beside the delegate built from them so that a path can be explained.
/// </summary>
internal sealed record RuleSet<TContext>(bool IsArray, JsonObserverItem<TContext>[] Items, JsonValuePolicy<TContext>? ValuePolicy);

/// <summary>
/// What a rule tests and what it does, for <see cref="PathExplanation"/>.
/// </summary>
/// <param name="Match">The test, for example <c>Path("card", "number")</c>.</param>
/// <param name="Action">The action, for example <c>Mask("***")</c>.</param>
/// <param name="Outcome">What the action does to the value.</param>
/// <param name="Child">Rules for the matched object or array; <c>null</c> for a value rule or a custom container rule.</param>
/// <param name="KeepsNull">The action writes <c>null</c> for a <c>null</c> value without masking it.</param>
internal sealed record RuleInfo<TContext>(string Match, string Action, PathOutcome Outcome, RuleSet<TContext>? Child = null, bool KeepsNull = false)
{
    public static RuleInfo<TContext> Unknown { get; } = new("rule", "custom rule", PathOutcome.Custom);
}
