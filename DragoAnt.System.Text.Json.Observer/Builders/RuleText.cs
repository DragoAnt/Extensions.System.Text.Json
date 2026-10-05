namespace DragoAnt.System.Text.Json.Observer.Builders;

/// <summary>
/// How rule actions read in a <see cref="PathExplanation"/>.
/// </summary>
internal static class RuleText
{
    public const string Unmasked = "Unmasked()";
    public const string CustomValue = "MaskValue(custom rule)";
    public const string ReadStr = "ReadStr (a string or null is read)";
    public const string ReadBool = "ReadBool (a boolean or null is read)";
    public const string ReadRaw = "ReadRaw (a scalar is read)";

    public static string ReadNumber(string method) => $"{method} (a number or null is read)";

    public static string Function(string? constant, MaskNulls nulls)
    {
        var what = constant is null ? "function" : $"\"{constant}\"";
        return nulls == MaskNulls.Mask ? $"Mask({what}, MaskNulls.Mask)" : $"Mask({what})";
    }

    public static string Tag(MaskTag tag) =>
        tag.Key is null ? $"Mask(MaskTag.{tag.Kind})" : $"Mask(MaskTag.{tag.Kind}, key {tag.Key})";

    public static string Strategy(MaskTag tag) => $"Mask(strategy, MaskTag.{tag.Kind})";
}
