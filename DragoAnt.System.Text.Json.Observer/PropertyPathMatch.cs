using DragoAnt.System.Text.Json.Observer.Strategies;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Property path matching class.
/// </summary>
internal sealed class PropertyPathMatch
{
    public const StringComparison DefaultComparison = StringComparison.OrdinalIgnoreCase;
    private readonly NameMatcher[] _matches;

    public PropertyPathMatch(PropMatchingStrategy[] matches)
        : this(matches.Select(m => m.Matcher).ToArray())
    {
    }

    private PropertyPathMatch(NameMatcher[] matches)
    {
        if (matches.Length == 0)
        {
            throw new ArgumentException("Value cannot be an empty collection.", nameof(matches));
        }

        _matches = matches;
    }

    public (bool success, int depth) RelativeMatch(int depth, ref PropertyPath propPath)
    {
        var last = propPath.CurrentDepth;
        var j = -1;
        for (var i = _matches.Length - 1; i >= 0; i--)
        {
            j++;
            if (!_matches[i].Match(ref propPath, last - j))
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
            if (!_matches[i].Match(ref propPath, i + depth))
            {
                return (false, _matches.Length);
            }
        }

        return (true, _matches.Length);
    }
}
