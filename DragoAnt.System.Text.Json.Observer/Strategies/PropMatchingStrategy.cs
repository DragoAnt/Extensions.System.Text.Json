namespace DragoAnt.System.Text.Json.Observer.Strategies;

/// <summary>
/// A property name test. A string converts to an exact, case-insensitive match; <see cref="PropMatches"/> has the others.
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

    /// <summary>
    /// Matches property names with a custom test that honours <see cref="JsonObserverOptions.PropertyNameCaseInsensitive"/>;
    /// the name is decoded to a <see cref="string"/> for it.
    /// </summary>
    /// <param name="strategy">
    /// Name test; receives <c>null</c> for an array item, and <see cref="StringComparison.OrdinalIgnoreCase"/> or
    /// <see cref="StringComparison.Ordinal"/> as the call's case option.
    /// </param>
    public PropMatchingStrategy(Func<string?, StringComparison, bool> strategy)
    {
        _matcher = new NameMatcher.FuncNameMatcher(strategy);
    }

    internal PropMatchingStrategy(NameMatcher matcher)
    {
        _matcher = matcher;
    }

    internal NameMatcher Matcher => _matcher ?? NameMatcher.Never;

    /// <summary>
    /// The name test as a function of the decoded name.
    /// </summary>
    /// <param name="strategy">Name test.</param>
    public static implicit operator Func<string?, bool>(PropMatchingStrategy strategy) => strategy.Matcher.MatchString;
    /// <summary>
    /// A custom name test; the name is decoded to a <see cref="string"/> for it.
    /// </summary>
    /// <param name="strategy">Name test; receives <c>null</c> for an array item.</param>
    public static implicit operator PropMatchingStrategy(Func<string?, bool> strategy) => new(strategy);
    /// <summary>
    /// Matches the exact name, case-insensitively.
    /// </summary>
    /// <param name="pattern">Property name.</param>
    public static implicit operator PropMatchingStrategy(string pattern) => new(NameMatcher.Exact(pattern));
}
