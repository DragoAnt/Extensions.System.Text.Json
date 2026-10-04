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

    /// <summary>
    /// Masked the way a custom <see cref="Utf8MaskStrategy"/> decides from <see cref="MaskTag.Key"/>;
    /// the built-in strategy replaces it by <c>"***"</c>.
    /// </summary>
    Custom,
}

/// <summary>
/// Tag a rule passes to the <see cref="Utf8MaskStrategy"/>, so that one strategy serves every kind of masking.
/// </summary>
/// <param name="Kind">
/// How the value is masked. A strategy that does not know <paramref name="Key"/> falls back to it, so a tag such as
/// <c>new MaskTag(MaskKind.Hash, classification)</c> is still hashed by the built-in strategy.
/// </param>
/// <param name="Key">
/// Optional classification of the value, for example a data classification of a compliance taxonomy or a redactor
/// name, that a custom strategy maps to its own masking. It is compared with <see cref="object.Equals(object)"/>,
/// so prefer immutable keys with value equality.
/// </param>
public readonly record struct MaskTag(MaskKind Kind, object? Key = null)
{
    /// <summary>
    /// A tag only a custom strategy interprets, by <paramref name="key"/>; the built-in strategy writes <c>"***"</c>.
    /// </summary>
    /// <param name="key">Classification the strategy maps to its masking.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <c>null</c>.</exception>
    public static MaskTag Custom(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new MaskTag(MaskKind.Custom, key);
    }

    /// <summary>
    /// Gets <see cref="Key"/> when it is a <typeparamref name="T"/>.
    /// </summary>
    /// <param name="key">The key; <c>default</c> when it is absent or of another type.</param>
    /// <typeparam name="T">Expected key type.</typeparam>
    /// <returns><c>true</c> when the key is a <typeparamref name="T"/>.</returns>
    public bool TryGetKey<T>(out T? key)
    {
        if (Key is T typed)
        {
            key = typed;
            return true;
        }

        key = default;
        return false;
    }

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

    /// <summary>
    /// Lets a <see cref="MaskKind"/> stand for its tag.
    /// </summary>
    /// <param name="kind">How the value is masked.</param>
    public static implicit operator MaskTag(MaskKind kind) => new(kind);
}
