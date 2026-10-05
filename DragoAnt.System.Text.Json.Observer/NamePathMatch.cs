namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// A rule's name tests, one per level: from the enclosing object down (absolute) or at the end of the path (any depth).
/// </summary>
internal sealed class NamePathMatch
{
    private readonly NameMatch[] _matches;
    private readonly bool _isPath;

    public NamePathMatch(NameMatch[] matches, bool isPath)
    {
        if (matches.Length == 0)
        {
            throw new ArgumentException("Value cannot be an empty collection.", nameof(matches));
        }

        _matches = matches;
        _isPath = isPath;
    }

    public string Describe() => $"{(_isPath ? "Path" : "Match")}({string.Join(", ", _matches.Select(m => m.ToString()))})";

    public (bool success, int depth) RelativeMatch(int depth, ref JsonWalk walk)
    {
        var last = walk.CurrentDepth;
        var j = -1;
        for (var i = _matches.Length - 1; i >= 0; i--)
        {
            j++;
            if (!_matches[i].IsMatch(in walk.Path, last - j))
            {
                return (false, _matches.Length);
            }
        }

        return (true, _matches.Length);
    }

    public (bool success, int depth) AbsoluteMatch(int depth, ref JsonWalk walk)
    {
        for (var i = 0; i < _matches.Length; i++)
        {
            if (!_matches[i].IsMatch(in walk.Path, i + depth))
            {
                return (false, _matches.Length);
            }
        }

        return (true, _matches.Length);
    }
}
