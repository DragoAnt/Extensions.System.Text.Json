using DragoAnt.System.Text.Json.Observer.Strategies;

namespace DragoAnt.System.Text.Json.Observer.Builders;

/// <summary>
/// How rule actions read in a <see cref="JsonPathExplanation"/>.
/// </summary>
internal static class RuleText
{
    public const string Unmasked = "Unmasked()";
    public const string CustomValue = "MaskValue(custom rule)";
    public const string ReadStr = "ReadStr (a string or null is read)";
    public const string ReadBool = "ReadBool (a boolean or null is read)";
    public const string ReadRaw = "ReadRaw (a scalar is read)";

    public static string ReadNumber(string method) => $"{method} (a number or null is read)";

    public static string Strategy(string method, string? constant) =>
        constant is null ? $"{method}(function)" : $"{method}(\"{constant}\")";

    public static string Tag(MaskTag tag) =>
        tag.Key is null ? $"MaskAny(MaskTag.{tag.Kind})" : $"MaskAny(MaskTag.{tag.Kind}, key {tag.Key})";
}
