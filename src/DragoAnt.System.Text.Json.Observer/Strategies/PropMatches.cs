using System.Text.RegularExpressions;

namespace DragoAnt.System.Text.Json.Observer.Strategies;

/// <summary>
/// Property matching strategies.
/// </summary>
public static class PropMatches
{
    /// <summary>
    /// Matches property name by start value.
    /// </summary>
    /// <param name="value">Property name start value.</param>
    public static PropMatchingStrategy StartsWith(string value) => new(v => v?.StartsWith(value, PropertyPathMatch.DefaultComparison) == true);

    /// <summary>
    /// Matches property name by ending.
    /// </summary>
    /// <param name="value">Property name ending value.</param>
    public static PropMatchingStrategy EndsWith(string value) => new(v => v?.EndsWith(value, PropertyPathMatch.DefaultComparison) == true);

    /// <summary>
    /// Matches property name by containing value.
    /// </summary>
    /// <param name="value">Property name containing value.</param>
    public static PropMatchingStrategy Contains(string value) => new(v => v?.Contains(value, PropertyPathMatch.DefaultComparison) == true);

    /// <summary>
    /// Matches property name by regular expression.
    /// </summary>
    /// <param name="regex">Property name regular expression.</param>
    public static PropMatchingStrategy Regex(Regex regex) => new(v => v is not null && regex.IsMatch(v));

    /// <summary>
    /// Matches property by full name equality.
    /// </summary>
    /// <param name="propNames">Property names.</param>
    public static PropMatchingStrategy OneOf(params string[] propNames) =>
        new(v => propNames.Any(p => string.Equals(v, p, PropertyPathMatch.DefaultComparison)));
}
