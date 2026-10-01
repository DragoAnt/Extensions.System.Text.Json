namespace DragoAnt.System.Text.Json.Observer.Strategies;

/// <summary>
/// Property matching strategy.
/// </summary>
public readonly struct PropMatchingStrategy
{
    private readonly NameMatcher? _matcher;

    /// <summary>
    /// Matches property names with a custom test; the name is decoded to a <see cref="string"/> for it.
    /// </summary>
    /// <param name="strategy">Name test; receives <c>null</c> for an array item.</param>
    public PropMatchingStrategy(Func<string?, bool> strategy)
    {
        _matcher = new NameMatcher.FuncNameMatcher(strategy);
    }

    internal PropMatchingStrategy(NameMatcher matcher)
    {
        _matcher = matcher;
    }

    internal NameMatcher Matcher => _matcher ?? NameMatcher.Never;

    public static implicit operator Func<string?, bool>(PropMatchingStrategy strategy) => strategy.Matcher.MatchString;
    public static implicit operator PropMatchingStrategy(Func<string?, bool> strategy) => new(strategy);
    public static implicit operator PropMatchingStrategy(string pattern) => new(NameMatcher.Exact(pattern));
}
