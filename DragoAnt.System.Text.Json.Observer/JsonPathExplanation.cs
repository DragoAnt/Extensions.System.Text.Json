namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// What an observer does with a value, as reported by <see cref="JsonObserver.Explain"/>.
/// </summary>
public enum JsonPathOutcome
{
    /// <summary>
    /// The value is written as it is.
    /// </summary>
    Unchanged,

    /// <summary>
    /// The value is replaced: masked, hashed, written as <c>null</c>, or an object or array masked whole.
    /// </summary>
    Masked,

    /// <summary>
    /// The value is handed to the context by a read rule and written as it is.
    /// </summary>
    Read,

    /// <summary>
    /// A custom rule or policy decides; the observer cannot tell what it writes.
    /// </summary>
    Custom,

    /// <summary>
    /// A payload with this structure is not masked at all: its status is <see cref="MaskStatus.Invalid"/>.
    /// </summary>
    Invalid,
}

/// <summary>
/// Which rule or policy of an observer handles a JSON path, and what it does with the value there.
/// </summary>
/// <param name="Path">The path explained, normalized, for example <c>lines[0].qty</c>.</param>
/// <param name="Outcome">What happens to the value.</param>
/// <param name="Rule">The rule or policy that decides, for example <c>Match("qty")</c> or <c>default policy AllowList</c>.</param>
/// <param name="Action">What it does, for example <c>MaskAny("***")</c> or <c>writes "***"</c>.</param>
/// <param name="Steps">How the observer gets there, one entry per level of the path.</param>
public sealed record JsonPathExplanation(string Path, JsonPathOutcome Outcome, string Rule, string Action, IReadOnlyList<string> Steps)
{
    /// <summary>
    /// One line, for example <c>lines[0].qty: Masked by Match("qty") → MaskAny("***")</c>.
    /// </summary>
    public override string ToString() => $"{Path}: {Outcome} by {Rule} → {Action}";
}
