namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Settings of an observer built from a <see cref="JsonShape"/>.
/// </summary>
public sealed record JsonShapeOptions
{
    /// <summary>
    /// Defaults: unknown properties masked whole as <c>"***"</c>, <c>null</c> kept, names matched as the call says.
    /// </summary>
    public static JsonShapeOptions Default { get; } = new();

    /// <summary>
    /// What happens to a property the shape does not know; <see cref="UnknownMemberPolicy.MaskWhole"/> by default.
    /// </summary>
    public UnknownMemberPolicy Unknown { get; init; } = UnknownMemberPolicy.MaskWhole;

    /// <summary>
    /// How the values of unknown properties are masked, with <see cref="UnknownMemberPolicy.MaskWhole"/> and
    /// <see cref="UnknownMemberPolicy.Descend"/>; <see cref="MaskTag.Full"/> (<c>"***"</c>) by default.
    /// </summary>
    public MaskTag UnknownTag { get; init; } = MaskTag.Full;

    /// <summary>
    /// Write <c>null</c> as <c>null</c> where a value would be masked; otherwise it is masked too. On by default.
    /// </summary>
    public bool KeepNulls { get; init; } = true;

    /// <summary>
    /// Match property names ignoring case; <c>null</c> follows <see cref="ObserverOptions.NameCaseInsensitive"/> of
    /// the call, which ignores case by default. A property whose name does not match is unknown, so with
    /// <see cref="UnknownMemberPolicy.PassThrough"/> a differently cased sensitive property is written unchanged.
    /// </summary>
    public bool? NameCaseInsensitive { get; init; }

    /// <summary>
    /// Options that match property names the way the serializer does, typically those the shape was built from
    /// (<c>JsonTypeInfo.Options</c>).
    /// </summary>
    /// <param name="serializerOptions">Options whose <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serializerOptions"/> is <c>null</c>.</exception>
    public static JsonShapeOptions FromSerializerOptions(JsonSerializerOptions serializerOptions)
    {
        ArgumentNullException.ThrowIfNull(serializerOptions);
        return new JsonShapeOptions { NameCaseInsensitive = serializerOptions.PropertyNameCaseInsensitive };
    }
}
