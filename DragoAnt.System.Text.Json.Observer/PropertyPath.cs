using System.Buffers;
using System.Text;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Path of the value a rule is called for: one level per enclosing property or array item, from the root down.
/// </summary>
/// <remarks>
/// Valid only during the call it is passed to. Names are kept as UTF-8 and decoded only when asked for; an array item
/// keeps its index, so <see cref="ToString"/> renders <c>items[2].sku</c>.
/// </remarks>
public ref struct PropertyPath
{
    private readonly ReadOnlySpan<byte> _input;
    private Segment[] _segments;
    private byte[]? _scratch;
    private int _scratchUsed;

    internal PropertyPath(int capacity, ReadOnlySpan<byte> input)
    {
        _input = input;
        _segments = ArrayPool<Segment>.Shared.Rent(Math.Max(capacity, 1));
    }

    /// <summary>
    /// Current property path depth.
    /// </summary>
    private int Depth { get; set; } = -1;

    /// <summary>
    /// Capacity of internal array.
    /// </summary>
    internal int MaxLength { get; private set; }

    internal readonly int CurrentDepth => Depth;

    /// <summary>
    /// Number of levels in the path.
    /// </summary>
    public readonly int Length => Depth + 1;

    /// <summary>
    /// The input ended inside a value: every rule must stop reading.
    /// </summary>
    internal bool Stopped { get; private set; }

    internal void Stop() => Stopped = true;

    /// <summary>
    /// Adds the property name the reader stands on.
    /// </summary>
    internal void AddPropertyName(ref Utf8JsonReader reader)
    {
        ref var segment = ref Push();
        if (!reader.HasValueSequence && !reader.ValueIsEscaped && !_input.IsEmpty)
        {
            segment = new Segment(SegmentKind.Input, checked((int)reader.TokenStartIndex + 1), reader.ValueSpan.Length);
            return;
        }

        var maxLength = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        var start = Reserve(maxLength);
        var written = reader.CopyString(_scratch.AsSpan(start, maxLength));
        _scratchUsed = start + written;
        segment = new Segment(SegmentKind.Scratch, start, written);
    }

    /// <summary>
    /// Adds an unescaped UTF-8 property name.
    /// </summary>
    internal void AddPropertyName(ReadOnlySpan<byte> utf8Name)
    {
        ref var segment = ref Push();
        var start = Reserve(utf8Name.Length);
        utf8Name.CopyTo(_scratch.AsSpan(start));
        _scratchUsed = start + utf8Name.Length;
        segment = new Segment(SegmentKind.Scratch, start, utf8Name.Length);
    }

    /// <summary>
    /// Adds the array item at <paramref name="index"/>.
    /// </summary>
    internal void AddArrayItem(int index)
    {
        ref var segment = ref Push();
        segment = new Segment(SegmentKind.ArrayItem, index, 0);
    }

    internal void RemovePropertyName()
    {
        if (Depth <= -1)
        {
            return;
        }

        ref var segment = ref _segments[Depth];
        if (segment.Kind == SegmentKind.Scratch)
        {
            _scratchUsed = segment.Start;
        }

        segment = default;
        Depth--;
    }

    /// <summary>
    /// Gets the unescaped UTF-8 name of a level without decoding it.
    /// </summary>
    /// <param name="index">Level, from 0 to <see cref="Length"/> - 1.</param>
    /// <param name="utf8Name">The name; valid only during the call.</param>
    /// <returns><c>false</c> for an array item or an index out of range.</returns>
    public readonly bool TryGetPropertyNameUtf8(int index, out ReadOnlySpan<byte> utf8Name)
    {
        if (index < 0 || index > Depth)
        {
            utf8Name = default;
            return false;
        }

        var segment = _segments[index];
        switch (segment.Kind)
        {
            case SegmentKind.Input:
                utf8Name = _input.Slice(segment.Start, segment.Length);
                return true;
            case SegmentKind.Scratch:
                utf8Name = _scratch.AsSpan(segment.Start, segment.Length);
                return true;
            default:
                utf8Name = default;
                return false;
        }
    }

    /// <summary>
    /// Whether the level at <paramref name="index"/> is an array item.
    /// </summary>
    /// <param name="index">Level, from 0 to <see cref="Length"/> - 1.</param>
    public readonly bool IsArrayItem(int index) => index >= 0 && index <= Depth && _segments[index].Kind == SegmentKind.ArrayItem;

    /// <summary>
    /// Gets the zero-based position of an array item level within its array.
    /// </summary>
    /// <param name="index">Level, from 0 to <see cref="Length"/> - 1.</param>
    /// <param name="arrayIndex">Position of the item; -1 when the level is not an array item.</param>
    /// <returns><c>true</c> when the level is an array item.</returns>
    public readonly bool TryGetArrayIndex(int index, out int arrayIndex)
    {
        if (IsArrayItem(index))
        {
            arrayIndex = _segments[index].Start;
            return true;
        }

        arrayIndex = -1;
        return false;
    }

    /// <summary>
    /// UTF-8 name of the deepest level.
    /// </summary>
    internal readonly ReadOnlySpan<byte> CurrentUtf8 => TryGetPropertyNameUtf8(Depth, out var name) ? name : default;

    /// <summary>
    /// Name of the level at <paramref name="index"/>, 0 being the root's property.
    /// </summary>
    /// <param name="index">Level, from 0 to <see cref="Length"/> - 1.</param>
    /// <returns>The decoded name; <c>null</c> for an array item or an index out of range.</returns>
    public string? GetPropertyName(int index)
    {
        if (index < 0 || index > Depth)
        {
            return null;
        }

        ref var segment = ref _segments[index];
        if (segment.Decoded is not null || segment.Kind == SegmentKind.ArrayItem)
        {
            return segment.Decoded;
        }

        TryGetPropertyNameUtf8(index, out var utf8);
        return segment.Decoded = Encoding.UTF8.GetString(utf8);
    }

    /// <summary>
    /// Name of a level counted from the deepest one.
    /// </summary>
    /// <param name="reversedIndex">0 for the value's own name, 1 for its parent, and so on.</param>
    /// <returns>The decoded name; <c>null</c> for an array item or an index out of range.</returns>
    public string? GetPropertyNameReverse(int reversedIndex) => GetPropertyName(Depth - reversedIndex);

    /// <summary>
    /// The path from the root down: names joined with dots and array items as <c>[index]</c>, for example
    /// <c>items[2].sku</c>; a name that is empty or holds <c>.</c>, <c>[</c>, <c>]</c> or <c>'</c> is written as <c>['name']</c>.
    /// </summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        for (var i = 0; i <= Depth; i++)
        {
            if (TryGetArrayIndex(i, out var arrayIndex))
            {
                text.Append('[').Append(arrayIndex).Append(']');
                continue;
            }

            AppendName(text, GetPropertyName(i)!, first: i == 0);
        }

        return text.ToString();
    }

    internal static void AppendName(StringBuilder text, string name, bool first)
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
    /// Returns the pooled buffers. The path must not be used afterwards.
    /// </summary>
    internal void Dispose()
    {
        var segments = _segments;
        _segments = [];
        if (segments.Length > 0)
        {
            ArrayPool<Segment>.Shared.Return(segments, clearArray: true);
        }

        var scratch = _scratch;
        _scratch = null;
        if (scratch is not null)
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private ref Segment Push()
    {
        Depth++;
        MaxLength = Math.Max(MaxLength, Depth + 1);

        if (Depth >= _segments.Length)
        {
            var grown = ArrayPool<Segment>.Shared.Rent(Math.Max(MaxLength, _segments.Length * 2));
            _segments.AsSpan().CopyTo(grown);
            if (_segments.Length > 0)
            {
                ArrayPool<Segment>.Shared.Return(_segments, clearArray: true);
            }

            _segments = grown;
        }

        return ref _segments[Depth];
    }

    private int Reserve(int length)
    {
        var start = _scratchUsed;
        var required = start + length;
        if (_scratch is null || required > _scratch.Length)
        {
            var grown = ArrayPool<byte>.Shared.Rent(Math.Max(required, 256));
            if (_scratch is not null)
            {
                _scratch.AsSpan(0, start).CopyTo(grown);
                ArrayPool<byte>.Shared.Return(_scratch);
            }

            _scratch = grown;
        }

        return start;
    }

    internal enum SegmentKind : byte
    {
        ArrayItem,
        Input,
        Scratch,
    }

    internal struct Segment(SegmentKind kind, int start, int length)
    {
        public readonly SegmentKind Kind = kind;
        public readonly int Start = start;
        public readonly int Length = length;
        public string? Decoded;
    }
}
