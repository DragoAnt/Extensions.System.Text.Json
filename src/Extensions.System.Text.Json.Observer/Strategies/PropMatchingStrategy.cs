namespace Extensions.System.Text.Json.Observer.Strategies;

/// <summary>
/// Property matching strategy.
/// </summary>
public readonly struct PropMatchingStrategy(Func<string?, bool> strategy)
{
    private Func<string?, bool> Strategy { get; } = strategy;

    public static implicit operator Func<string?, bool>(PropMatchingStrategy strategy) => strategy.Strategy;
    public static implicit operator PropMatchingStrategy(Func<string?, bool> strategy) => new(strategy);
    public static implicit operator PropMatchingStrategy(string pattern) => new(v => PropertyPathMatch.DefaultPropertyNameEquals(pattern, v));
}
