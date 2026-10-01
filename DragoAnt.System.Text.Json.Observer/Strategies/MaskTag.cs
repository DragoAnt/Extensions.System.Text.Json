namespace DragoAnt.System.Text.Json.Observer.Strategies;

/// <summary>
/// How a sensitive value is masked.
/// </summary>
public enum MaskKind
{
    /// <summary>
    /// Replaced by <c>"***"</c>.
    /// </summary>
    Full,

    /// <summary>
    /// Only the last four characters are kept, as <c>"***1234"</c>; values shorter than eight characters are masked fully.
    /// </summary>
    Last4,

    /// <summary>
    /// Replaced by a keyed hash, as <c>"hash:0f3a…"</c>, so that equal values can be correlated without being shown.
    /// </summary>
    Hash,

    /// <summary>
    /// Replaced by <c>null</c>.
    /// </summary>
    Omit,
}

/// <summary>
/// Tag a rule passes to the <see cref="Utf8MaskStrategy"/>, so that one strategy serves every kind of masking.
/// </summary>
/// <param name="Kind">How the value is masked.</param>
public readonly record struct MaskTag(MaskKind Kind)
{
    /// <summary>
    /// <see cref="MaskKind.Full"/>.
    /// </summary>
    public static MaskTag Full => new(MaskKind.Full);

    /// <summary>
    /// <see cref="MaskKind.Last4"/>.
    /// </summary>
    public static MaskTag Last4 => new(MaskKind.Last4);

    /// <summary>
    /// <see cref="MaskKind.Hash"/>.
    /// </summary>
    public static MaskTag Hash => new(MaskKind.Hash);

    /// <summary>
    /// <see cref="MaskKind.Omit"/>.
    /// </summary>
    public static MaskTag Omit => new(MaskKind.Omit);

    public static implicit operator MaskTag(MaskKind kind) => new(kind);
}
