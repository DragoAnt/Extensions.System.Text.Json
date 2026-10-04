namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// What a shape-driven observer does with a property its shape does not know.
/// </summary>
public enum UnknownMemberPolicy
{
    /// <summary>
    /// The value is masked whole, as <c>"***"</c>, whatever its JSON type; its content is never read.
    /// </summary>
    MaskWhole,

    /// <summary>
    /// Objects and arrays are descended so that their property names stay visible; every value inside is masked.
    /// </summary>
    Descend,

    /// <summary>
    /// The value is written as is. Only the properties the shape marks as sensitive are masked.
    /// </summary>
    PassThrough,
}

/// <summary>
/// Settings of an observer built from a <see cref="JsonShape"/>.
/// </summary>
/// <param name="Unknown">What happens to a property the shape does not know.</param>
/// <param name="KeepNulls">Write <c>null</c> as <c>null</c> where a value would be masked; otherwise it is masked too.</param>
/// <param name="PropertyNameCaseInsensitive">
/// Match property names ignoring case; <c>null</c> follows <see cref="JsonObserverOptions.PropertyNameCaseInsensitive"/>
/// of the call, which ignores case by default. A property whose name does not match is unknown, so with
/// <see cref="UnknownMemberPolicy.PassThrough"/> a differently cased sensitive property is written unchanged.
/// </param>
public sealed record JsonShapeOptions(
    UnknownMemberPolicy Unknown = UnknownMemberPolicy.MaskWhole,
    bool KeepNulls = true,
    bool? PropertyNameCaseInsensitive = null)
{
    /// <summary>
    /// Defaults: unknown properties masked whole, <c>null</c> kept, names matched as the call says.
    /// </summary>
    public static JsonShapeOptions Default { get; } = new();

    /// <summary>
    /// Options that match property names the way the serializer does, typically those the shape was built from
    /// (<c>JsonTypeInfo.Options</c>).
    /// </summary>
    /// <param name="serializerOptions">Options whose <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> is used.</param>
    /// <param name="unknown">What happens to a property the shape does not know.</param>
    /// <param name="keepNulls">Write <c>null</c> as <c>null</c> where a value would be masked.</param>
    public static JsonShapeOptions FromSerializerOptions(
        JsonSerializerOptions serializerOptions,
        UnknownMemberPolicy unknown = UnknownMemberPolicy.MaskWhole,
        bool keepNulls = true)
    {
        ArgumentNullException.ThrowIfNull(serializerOptions);
        return new JsonShapeOptions(unknown, keepNulls, serializerOptions.PropertyNameCaseInsensitive);
    }
}
