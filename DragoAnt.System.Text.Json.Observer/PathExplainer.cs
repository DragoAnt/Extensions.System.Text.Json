using System.Globalization;
using System.Text;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// One level of a path given to <c>Explain</c>: a property name, or an array index when <see cref="Name"/> is <c>null</c>.
/// </summary>
internal readonly record struct PathSegment(string? Name, int Index)
{
    public bool IsIndex => Name is null;
}

/// <summary>
/// Explains which rule or policy of an observer handles a path.
/// </summary>
internal abstract class PathExplainer
{
    public PathExplanation Explain(string path, ValueKind valueKind, bool propertyNameCaseInsensitive)
    {
        ArgumentNullException.ThrowIfNull(path);
        var token = valueKind switch
        {
            ValueKind.String => JsonTokenType.String,
            ValueKind.Number => JsonTokenType.Number,
            ValueKind.Boolean => JsonTokenType.True,
            ValueKind.Null => JsonTokenType.Null,
            ValueKind.Object => JsonTokenType.StartObject,
            ValueKind.Array => JsonTokenType.StartArray,
            _ => throw new ArgumentOutOfRangeException(nameof(valueKind), valueKind, "Expected a defined value kind."),
        };

        var segments = Parse(path);
        var normalized = Format(segments);
        if (segments.Count == 0)
        {
            return new PathExplanation { Path = "$", Outcome = PathOutcome.Unchanged, Rule = "root", Action = "the root's rules apply to its members" };
        }

        var steps = new List<string>();
        var (outcome, rule, action) = Explain(segments, token, propertyNameCaseInsensitive, steps);
        return new PathExplanation { Path = normalized, Outcome = outcome, Rule = rule, Action = action, Steps = steps };
    }

    protected abstract (PathOutcome Outcome, string Rule, string Action) Explain(
        IReadOnlyList<PathSegment> segments,
        JsonTokenType valueKind,
        bool propertyNameCaseInsensitive,
        List<string> steps);

    protected static JsonTokenType TokenAt(IReadOnlyList<PathSegment> segments, int index, JsonTokenType valueKind) =>
        index == segments.Count - 1 ? valueKind
        : segments[index + 1].IsIndex ? JsonTokenType.StartArray
        : JsonTokenType.StartObject;

    protected static JsonWalk PathOf(IReadOnlyList<PathSegment> segments, bool propertyNameCaseInsensitive) =>
        new(segments.Count, default, JsonObserverOptions.Default with { NameCaseInsensitive = propertyNameCaseInsensitive });

    protected static void Push(ref JsonWalk path, PathSegment segment)
    {
        if (segment.IsIndex)
        {
            path.AddArrayItem(segment.Index);
            return;
        }

        var name = segment.Name!;
        var buffer = Encoding.UTF8.GetBytes(name);
        path.AddPropertyName(buffer);
    }

    protected static string Format(IReadOnlyList<PathSegment> segments, int count = -1)
    {
        var text = new StringBuilder();
        count = count < 0 ? segments.Count : count;
        for (var i = 0; i < count; i++)
        {
            if (segments[i].IsIndex)
            {
                text.Append('[').Append(segments[i].Index.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            else
            {
                AppendName(text, segments[i].Name!, first: i == 0);
            }
        }

        return text.ToString();
    }

    private static void AppendName(StringBuilder text, string name, bool first)
    {
        if (name.Length == 0 || name.AsSpan().IndexOfAny(".[]'") >= 0)
        {
            text.Append("['").Append(name.Replace("'", "\\'", StringComparison.Ordinal)).Append("']");
            return;
        }

        if (!first)
        {
            text.Append('.');
        }

        text.Append(name);
    }

    /// <summary>
    /// Parses <c>$.a.b[2]['c.d']</c>; the leading <c>$</c> and the first dot are optional.
    /// </summary>
    internal static List<PathSegment> Parse(string path)
    {
        var segments = new List<PathSegment>();
        var i = 0;
        if (path.StartsWith('$'))
        {
            i = 1;
        }

        var expectName = i == 0;
        while (i < path.Length)
        {
            var c = path[i];
            if (c == '.')
            {
                if (expectName)
                {
                    throw Invalid(path, i);
                }

                i++;
                expectName = true;
                continue;
            }

            if (c == '[')
            {
                i = ParseBracket(path, i, segments);
                expectName = false;
                continue;
            }

            if (!expectName)
            {
                throw Invalid(path, i);
            }

            var start = i;
            while (i < path.Length && path[i] is not ('.' or '['))
            {
                if (path[i] is '*' or ':')
                {
                    throw Invalid(path, i);
                }

                i++;
            }

            segments.Add(new PathSegment(path[start..i], -1));
            expectName = false;
        }

        if (expectName && path.Length > 0 && path[^1] == '.')
        {
            throw Invalid(path, path.Length - 1);
        }

        return segments;
    }

    private static int ParseBracket(string path, int i, List<PathSegment> segments)
    {
        var start = i + 1;
        if (start < path.Length && path[start] is '\'' or '"')
        {
            var quote = path[start];
            var name = new StringBuilder();
            var j = start + 1;
            while (j < path.Length && path[j] != quote)
            {
                if (path[j] == '\\' && j + 1 < path.Length)
                {
                    j++;
                }

                name.Append(path[j++]);
            }

            if (j + 1 >= path.Length || path[j + 1] != ']')
            {
                throw Invalid(path, i);
            }

            segments.Add(new PathSegment(name.ToString(), -1));
            return j + 2;
        }

        var end = path.IndexOf(']', start);
        if (end < 0 || !int.TryParse(path.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            throw Invalid(path, i);
        }

        segments.Add(new PathSegment(null, index));
        return end + 1;
    }

    private static ArgumentException Invalid(string path, int at) =>
        new($"'{path}' is not a JSON path such as 'items[2].sku' or \"$['a.b']\" (position {at}).", nameof(path));
}
