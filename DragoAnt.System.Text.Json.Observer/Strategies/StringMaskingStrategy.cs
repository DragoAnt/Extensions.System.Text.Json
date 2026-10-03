using System.Text.RegularExpressions;

namespace DragoAnt.System.Text.Json.Observer.Strategies;

/// <summary>
/// Replacement of a masked value: a constant string, a regular expression whose matches become <c>*</c>, or a function.
/// </summary>
/// <param name="strategy">Returns the replacement for a value; <c>null</c> writes <c>null</c>.</param>
public readonly ref struct StringMaskingStrategy<TContext>(Func<string?, TContext, string?> strategy)
{
    private Func<string?, TContext, string?> Strategy { get; } = strategy;

    /// <summary>
    /// The replacement as a function.
    /// </summary>
    /// <param name="strategy">Replacement.</param>
    public static implicit operator Func<string?, TContext, string?>(StringMaskingStrategy<TContext> strategy) => strategy.Strategy;

    /// <summary>
    /// A replacement computed from the value and the context.
    /// </summary>
    /// <param name="strategy">Returns the replacement for a value; <c>null</c> writes <c>null</c>.</param>
    public static implicit operator StringMaskingStrategy<TContext>(Func<string?, TContext, string?> strategy) => new(strategy);

    /// <summary>
    /// Replaces every match of <paramref name="strategy"/> in the value with <c>*</c>.
    /// </summary>
    /// <param name="strategy">Text to hide.</param>
    public static implicit operator StringMaskingStrategy<TContext>(Regex strategy) => Regex(strategy);

    /// <summary>
    /// Replaces every value with the same text.
    /// </summary>
    /// <param name="strategy">Replacement, for example <c>"***"</c>.</param>
    public static implicit operator StringMaskingStrategy<TContext>(string strategy) => new((_, _) => strategy);

    /// <summary>
    /// Replaces every match of <paramref name="strategy"/> in the value with <paramref name="replacement"/>; <c>null</c> stays <c>null</c>.
    /// </summary>
    /// <param name="strategy">Text to hide.</param>
    /// <param name="replacement">Replacement pattern, as for <see cref="global::System.Text.RegularExpressions.Regex.Replace(string, string)"/>.</param>
    public static StringMaskingStrategy<TContext> Regex(Regex strategy, string replacement = "*") =>
        new((v, _) => v is null ? null : strategy.Replace(v, replacement));

    /// <summary>
    /// Replaces every match of <paramref name="strategy"/> in the value with what <paramref name="evaluator"/> returns;
    /// <c>null</c> stays <c>null</c>.
    /// </summary>
    /// <param name="strategy">Text to hide.</param>
    /// <param name="evaluator">Returns the replacement of one match.</param>
    public static StringMaskingStrategy<TContext> Regex(Regex strategy, MatchEvaluator evaluator) =>
        new((v, _) => v is null ? null : strategy.Replace(v, evaluator));
}
