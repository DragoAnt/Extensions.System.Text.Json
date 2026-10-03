using System.Text;

namespace DragoAnt.System.Text.Json.Observer.Strategies;

/// <summary>
/// Property name test that works on the UTF-8 name and decodes it only when it has to.
/// </summary>
internal abstract class NameMatcher
{
    public static readonly NameMatcher Never = new FuncNameMatcher(_ => false);

    public abstract string Describe();

    public bool MatchString(string? name) => MatchString(name, PropertyPathMatch.DefaultComparison);

    public abstract bool MatchString(string? name, StringComparison comparison);

    public virtual bool Match(ref PropertyPath path, int index) => MatchString(path.GetPropertyName(index), ComparisonOf(ref path));

    private protected static StringComparison ComparisonOf(ref PropertyPath path) =>
        path.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static NameMatcher Exact(string pattern) => new TextNameMatcher(TextNameMatcher.Mode.Equals, pattern);

    public static NameMatcher StartsWith(string pattern) => new TextNameMatcher(TextNameMatcher.Mode.StartsWith, pattern);

    public static NameMatcher EndsWith(string pattern) => new TextNameMatcher(TextNameMatcher.Mode.EndsWith, pattern);

    public static NameMatcher Contains(string pattern) => new TextNameMatcher(TextNameMatcher.Mode.Contains, pattern);

    public static NameMatcher OneOf(string[] names) => new OneOfNameMatcher(names);

    internal sealed class FuncNameMatcher(Func<string?, bool> match, string? description = null) : NameMatcher
    {
        public override string Describe() => description ?? "custom name test";

        public override bool MatchString(string? name, StringComparison comparison) => match(name);
    }

    /// <summary>
    /// Pattern compared byte by byte when both it and the name are ASCII; any other name falls back to the string comparison.
    /// </summary>
    private sealed class TextNameMatcher(TextNameMatcher.Mode mode, string pattern) : NameMatcher
    {
        private readonly byte[]? _ascii = Ascii.IsValid(pattern) ? Encoding.ASCII.GetBytes(pattern) : null;

        public override string Describe() => mode switch
        {
            Mode.Equals => $"\"{pattern}\"",
            _ => $"{mode}(\"{pattern}\")",
        };

        public override bool MatchString(string? name, StringComparison comparison) => name is not null && mode switch
        {
            Mode.Equals => string.Equals(name, pattern, comparison),
            Mode.StartsWith => name.StartsWith(pattern, comparison),
            Mode.EndsWith => name.EndsWith(pattern, comparison),
            _ => name.Contains(pattern, comparison),
        };

        public override bool Match(ref PropertyPath path, int index)
        {
            if (!path.TryGetPropertyNameUtf8(index, out var name))
            {
                return false;
            }

            if (_ascii is null || !Ascii.IsValid(name))
            {
                return MatchString(path.GetPropertyName(index), ComparisonOf(ref path));
            }

            ReadOnlySpan<byte> utf8 = _ascii;
            var ignoreCase = path.PropertyNameCaseInsensitive;
            return mode switch
            {
                Mode.Equals => name.Length == utf8.Length && SameText(name, utf8, ignoreCase),
                Mode.StartsWith => name.Length >= utf8.Length && SameText(name[..utf8.Length], utf8, ignoreCase),
                Mode.EndsWith => name.Length >= utf8.Length && SameText(name[^utf8.Length..], utf8, ignoreCase),
                _ => ContainsText(name, utf8, ignoreCase),
            };
        }

        private static bool ContainsText(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value, bool ignoreCase)
        {
            if (!ignoreCase)
            {
                return name.IndexOf(value) >= 0;
            }

            for (var i = 0; i + value.Length <= name.Length; i++)
            {
                if (Ascii.EqualsIgnoreCase(name.Slice(i, value.Length), value))
                {
                    return true;
                }
            }

            return false;
        }

        internal enum Mode : byte
        {
            Equals,
            StartsWith,
            EndsWith,
            Contains,
        }
    }

    private static bool SameText(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, bool ignoreCase) =>
        ignoreCase ? Ascii.EqualsIgnoreCase(left, right) : left.SequenceEqual(right);

    private sealed class OneOfNameMatcher : NameMatcher
    {
        private readonly string[] _names;
        private readonly HashSet<string> _ignoreCase;
        private readonly HashSet<string> _exact;
        private readonly byte[][]? _asciiNames;

        public OneOfNameMatcher(string[] names)
        {
            _names = names;
            _ignoreCase = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            _exact = new HashSet<string>(names, StringComparer.Ordinal);
            _asciiNames = names.All(n => Ascii.IsValid(n)) ? names.Select(n => Encoding.ASCII.GetBytes(n)).ToArray() : null;
        }

        public override string Describe() => $"OneOf({string.Join(", ", _names.Select(n => $"\"{n}\""))})";

        public override bool MatchString(string? name, StringComparison comparison) =>
            name is not null && (comparison == StringComparison.Ordinal ? _exact : _ignoreCase).Contains(name);

        public override bool Match(ref PropertyPath path, int index)
        {
            if (!path.TryGetPropertyNameUtf8(index, out var name))
            {
                return false;
            }

            if (_asciiNames is null || !Ascii.IsValid(name))
            {
                return MatchString(path.GetPropertyName(index), ComparisonOf(ref path));
            }

            var ignoreCase = path.PropertyNameCaseInsensitive;
            foreach (var candidate in _asciiNames)
            {
                if (candidate.Length == name.Length && SameText(name, candidate, ignoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
