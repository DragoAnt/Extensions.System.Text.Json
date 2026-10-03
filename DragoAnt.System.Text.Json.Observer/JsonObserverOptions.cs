using DragoAnt.System.Text.Json.Observer.Strategies;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Limits and output settings of one masking call; the same for the string and the UTF-8 API.
/// </summary>
/// <param name="MaxOutputBytes">Output size limit in UTF-8 bytes; when reached the output is closed and the status is <see cref="MaskStatus.Truncated"/>.</param>
/// <param name="MaxValueBytes">
/// Longest string value written, in UTF-8 bytes; a longer one is cut, ends with an ellipsis and makes the status <see cref="MaskStatus.Truncated"/>.
/// A rule's masking function also receives a longer value cut to this length.
/// </param>
/// <param name="MaxDepth">Deepest nesting accepted; a deeper payload is <see cref="MaskStatus.Invalid"/>.</param>
/// <param name="RelaxedEscaping">Write non-ASCII and HTML-sensitive characters unescaped, which keeps logs readable.</param>
/// <param name="HashKey">
/// Key of <see cref="MaskKind.Hash"/>. When empty, a random key is used for the lifetime of the process,
/// so hashes correlate within one process only.
/// </param>
/// <param name="MaskStrategy">Strategy for rules added with a <see cref="MaskTag"/>; <see cref="Utf8MaskStrategy.Default"/> when <c>null</c>.</param>
/// <param name="IgnoreNulls">Drop properties and array items whose value is <c>null</c>, and objects and arrays left empty by that.</param>
/// <param name="Indented">Write the output indented.</param>
/// <param name="PropertyNameCaseInsensitive">
/// Match rule names, <see cref="PropMatches"/> tests and shape properties ignoring case, as by default. Pass the
/// <c>PropertyNameCaseInsensitive</c> of the serializer's options to match names the way deserialization does.
/// With <c>false</c> a rule no longer catches a differently cased name: under a block list such a value is written
/// unchanged, under an allow list it is masked.
/// </param>
public sealed record JsonObserverOptions(
    int MaxOutputBytes = int.MaxValue,
    int MaxValueBytes = int.MaxValue,
    int MaxDepth = 64,
    bool RelaxedEscaping = true,
    ReadOnlyMemory<byte> HashKey = default,
    Utf8MaskStrategy? MaskStrategy = null,
    bool IgnoreNulls = false,
    bool Indented = false,
    bool PropertyNameCaseInsensitive = true)
{
    /// <summary>
    /// Defaults: no size limits, depth 64, relaxed escaping, a per-process hash key, the built-in strategy,
    /// <c>null</c> values kept, compact output and names matched ignoring case.
    /// </summary>
    public static JsonObserverOptions Default { get; } = new();
}
