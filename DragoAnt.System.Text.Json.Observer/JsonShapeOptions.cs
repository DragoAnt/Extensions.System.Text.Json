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
public sealed record JsonShapeOptions(UnknownMemberPolicy Unknown = UnknownMemberPolicy.MaskWhole, bool KeepNulls = true)
{
    /// <summary>
    /// Defaults: unknown properties masked whole, <c>null</c> kept.
    /// </summary>
    public static JsonShapeOptions Default { get; } = new();
}
