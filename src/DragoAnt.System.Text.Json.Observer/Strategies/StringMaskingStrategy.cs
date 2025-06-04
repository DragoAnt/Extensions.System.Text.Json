using System.Text.RegularExpressions;

namespace DragoAnt.System.Text.Json.Observer.Strategies;

/// <summary>
/// String property masking strategy
/// </summary>
/// <param name="strategy">Masking function.</param>
public readonly ref struct StringMaskingStrategy<TContext>(Func<string?, TContext, string?> strategy)
{
    private Func<string?, TContext, string?> Strategy { get; } = strategy;

    public static implicit operator Func<string?, TContext, string?>(StringMaskingStrategy<TContext> strategy) => strategy.Strategy;
    public static implicit operator StringMaskingStrategy<TContext>(Func<string?, TContext, string?> strategy) => new(strategy);
    public static implicit operator StringMaskingStrategy<TContext>(Regex strategy) => Regex(strategy);
    public static implicit operator StringMaskingStrategy<TContext>(string strategy) => new((_, _) => strategy);

    public static StringMaskingStrategy<TContext> Regex(Regex strategy, string replacement = "*") =>
        new((v, _) => v is null ? null : strategy.Replace(v, replacement));

    public static StringMaskingStrategy<TContext> Regex(Regex strategy, MatchEvaluator evaluator) =>
        new((v, _) => v is null ? null : strategy.Replace(v, evaluator));
}