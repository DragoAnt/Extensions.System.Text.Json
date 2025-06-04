using Extensions.System.Text.Json.Observer.Strategies;

namespace Extensions.System.Text.Json.Observer;

/// <summary>
/// Property path matching class.
/// </summary>
public sealed class PropertyPathMatch
{
    public const StringComparison DefaultComparison = StringComparison.OrdinalIgnoreCase;
    private readonly Func<string?, bool>[] _matches;

    public PropertyPathMatch(PropMatchingStrategy[] matches)
        : this(matches.Select(m => (Func<string?, bool>)m).ToArray())
    {
    }

    public PropertyPathMatch(params Func<string?, bool>[] matches)
    {
        if (matches.Length == 0)
        {
            throw new ArgumentException("Value cannot be an empty collection.", nameof(matches));
        }

        _matches = matches;
    }

    public (bool success, int depth) RelativeMatch(int depth, ref PropertyPath propPath)
    {
        int j = -1;
        for (var i = _matches.Length - 1; i >= 0; i--)
        {
            j++;
            if (!_matches[i](propPath.GetPropertyNameReverse(j)))
            {
                return (false, _matches.Length);
            }
        }

        return (true, _matches.Length);
    }

    public (bool success, int depth) AbsoluteMatch(int depth, ref PropertyPath propPath)
    {
        for (var i = 0; i < _matches.Length; i++)
        {
            if (!_matches[i](propPath.GetPropertyName(i + depth)))
            {
                return (false, _matches.Length);
            }
        }

        return (true, _matches.Length);
    }

    internal static bool DefaultPropertyNameEquals(string value, string? other) => string.Equals(value, other, DefaultComparison);
}