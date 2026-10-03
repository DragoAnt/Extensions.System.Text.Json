using System.Text;

namespace DragoAnt.System.Text.Json.Observer.Strategies;

/// <summary>
/// Property name test that works on the UTF-8 name and decodes it only when it has to.
/// </summary>
internal abstract class NameMatcher
{
    public static readonly NameMatcher Never = new FuncNameMatcher(_ => false);

    public abstract bool MatchString(string? name);

    public virtual bool Match(ref PropertyPath path, int index) => MatchString(path.GetPropertyName(index));

    public static NameMatcher Exact(string pattern) =>
        IsAscii(pattern) ? new AsciiNameMatcher(AsciiNameMatcher.Mode.Equals, pattern) : new FuncNameMatcher(v => PropertyPathMatch.DefaultPropertyNameEquals(pattern, v));

    public static NameMatcher StartsWith(string pattern) =>
        IsAscii(pattern)
            ? new AsciiNameMatcher(AsciiNameMatcher.Mode.StartsWith, pattern)
            : new FuncNameMatcher(v => v?.StartsWith(pattern, PropertyPathMatch.DefaultComparison) == true);

    public static NameMatcher EndsWith(string pattern) =>
        IsAscii(pattern)
            ? new AsciiNameMatcher(AsciiNameMatcher.Mode.EndsWith, pattern)
            : new FuncNameMatcher(v => v?.EndsWith(pattern, PropertyPathMatch.DefaultComparison) == true);

    public static NameMatcher Contains(string pattern) =>
        IsAscii(pattern)
            ? new AsciiNameMatcher(AsciiNameMatcher.Mode.Contains, pattern)
            : new FuncNameMatcher(v => v?.Contains(pattern, PropertyPathMatch.DefaultComparison) == true);

    public static NameMatcher OneOf(string[] names) => new OneOfNameMatcher(names);

    private static bool IsAscii(string value) => Ascii.IsValid(value);

    internal sealed class FuncNameMatcher(Func<string?, bool> match) : NameMatcher
    {
        public override bool MatchString(string? name) => match(name);
    }

    /// <summary>
    /// ASCII pattern compared to ASCII names byte by byte; any other name falls back to the string comparison.
    /// </summary>
    private sealed class AsciiNameMatcher(AsciiNameMatcher.Mode mode, string pattern) : NameMatcher
    {
        private readonly byte[] _utf8 = Encoding.ASCII.GetBytes(pattern);

        public override bool MatchString(string? name) => name is not null && mode switch
        {
            Mode.Equals => string.Equals(name, pattern, PropertyPathMatch.DefaultComparison),
            Mode.StartsWith => name.StartsWith(pattern, PropertyPathMatch.DefaultComparison),
            Mode.EndsWith => name.EndsWith(pattern, PropertyPathMatch.DefaultComparison),
            _ => name.Contains(pattern, PropertyPathMatch.DefaultComparison),
        };

        public override bool Match(ref PropertyPath path, int index)
        {
            if (!path.TryGetPropertyNameUtf8(index, out var name))
            {
                return false;
            }

            if (!Ascii.IsValid(name))
            {
                return MatchString(path.GetPropertyName(index));
            }

            ReadOnlySpan<byte> utf8 = _utf8;
            return mode switch
            {
                Mode.Equals => name.Length == utf8.Length && Ascii.EqualsIgnoreCase(name, utf8),
                Mode.StartsWith => name.Length >= utf8.Length && Ascii.EqualsIgnoreCase(name[..utf8.Length], utf8),
                Mode.EndsWith => name.Length >= utf8.Length && Ascii.EqualsIgnoreCase(name[^utf8.Length..], utf8),
                _ => ContainsIgnoreCase(name, utf8),
            };
        }

        private static bool ContainsIgnoreCase(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
        {
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

    private sealed class OneOfNameMatcher : NameMatcher
    {
        private readonly HashSet<string> _names;
        private readonly byte[][]? _asciiNames;

        public OneOfNameMatcher(string[] names)
        {
            _names = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            _asciiNames = names.All(n => Ascii.IsValid(n)) ? names.Select(n => Encoding.ASCII.GetBytes(n)).ToArray() : null;
        }

        public override bool MatchString(string? name) => name is not null && _names.Contains(name);

        public override bool Match(ref PropertyPath path, int index)
        {
            if (!path.TryGetPropertyNameUtf8(index, out var name))
            {
                return false;
            }

            if (_asciiNames is null || !Ascii.IsValid(name))
            {
                return MatchString(path.GetPropertyName(index));
            }

            foreach (var candidate in _asciiNames)
            {
                if (candidate.Length == name.Length && Ascii.EqualsIgnoreCase(name, candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
