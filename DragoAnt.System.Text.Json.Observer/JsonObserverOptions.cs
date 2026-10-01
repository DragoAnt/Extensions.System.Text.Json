namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Limits and output settings for masking a UTF-8 JSON payload.
/// </summary>
/// <param name="MaxOutputBytes">Output size limit; when reached the output is closed and the status is <see cref="MaskStatus.Truncated"/>.</param>
/// <param name="MaxValueBytes">Longest string value written as is; a longer one is cut and ends with an ellipsis.</param>
/// <param name="MaxDepth">Deepest nesting accepted; a deeper payload is <see cref="MaskStatus.Invalid"/>.</param>
/// <param name="RelaxedEscaping">Write non-ASCII and HTML-sensitive characters unescaped, which keeps logs readable.</param>
public sealed record JsonObserverOptions(
    int MaxOutputBytes = int.MaxValue,
    int MaxValueBytes = int.MaxValue,
    int MaxDepth = 64,
    bool RelaxedEscaping = true)
{
    /// <summary>
    /// Defaults: no size limits, depth 64, relaxed escaping.
    /// </summary>
    public static JsonObserverOptions Default { get; } = new();
}
