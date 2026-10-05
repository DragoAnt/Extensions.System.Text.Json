namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Limits and output settings of one JSON observer call; the same for the string and the UTF-8 API. The limits,
/// the hash key, the strategy and the case option are those of <see cref="ObserverOptions"/>.
/// </summary>
public sealed record JsonObserverOptions : ObserverOptions
{
    /// <summary>
    /// Defaults: no size limits, depth 64, relaxed escaping, compact output, a per-process hash key, the built-in
    /// strategy, <c>null</c> values kept and names matched ignoring case.
    /// </summary>
    public static new JsonObserverOptions Default { get; } = new();

    /// <summary>
    /// Write non-ASCII and HTML-sensitive characters unescaped, which keeps logs readable; on by default.
    /// </summary>
    public bool RelaxedEscaping { get; init; } = true;

    /// <summary>
    /// Write the output indented.
    /// </summary>
    public bool Indented { get; init; }
}
