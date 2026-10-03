using System.Buffers;
using System.Text;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Path of the value a rule is called for: one level per enclosing property or array item, from the root down.
/// </summary>
/// <remarks>
/// Valid only during the call it is passed to. Names are kept as UTF-8 and decoded only when asked for.
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
    /// Add property name considering depth.
    /// </summary>
    internal void AddPropertyName(string? name)
    {
        ref var segment = ref Push();
        segment = name is null
            ? new Segment(SegmentKind.ArrayItem, 0, 0)
            : new Segment(SegmentKind.Text, 0, 0) { Decoded = name };
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
    /// UTF-8 name of the level at <paramref name="index"/>; <c>false</c> for an array item or a level out of range.
    /// </summary>
    internal readonly bool TryGetUtf8(int index, out ReadOnlySpan<byte> name)
    {
        if (index < 0 || index > Depth)
        {
            name = default;
            return false;
        }

        var segment = _segments[index];
        switch (segment.Kind)
        {
            case SegmentKind.Input:
                name = _input.Slice(segment.Start, segment.Length);
                return true;
            case SegmentKind.Scratch:
                name = _scratch.AsSpan(segment.Start, segment.Length);
                return true;
            case SegmentKind.Text:
                name = Encoding.UTF8.GetBytes(segment.Decoded!);
                return true;
            default:
                name = default;
                return false;
        }
    }

    /// <summary>
    /// UTF-8 name of the deepest level.
    /// </summary>
    internal readonly ReadOnlySpan<byte> CurrentUtf8 => TryGetUtf8(Depth, out var name) ? name : default;

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

        TryGetUtf8(index, out var utf8);
        return segment.Decoded = Encoding.UTF8.GetString(utf8);
    }

    /// <summary>
    /// Name of a level counted from the deepest one.
    /// </summary>
    /// <param name="reversedIndex">0 for the value's own name, 1 for its parent, and so on.</param>
    /// <returns>The decoded name; <c>null</c> for an array item or an index out of range.</returns>
    public string? GetPropertyNameReverse(int reversedIndex) => GetPropertyName(Depth - reversedIndex);

    /// <summary>
    /// The names from the root down, joined with dots; an array item is an empty segment.
    /// </summary>
    public override string ToString()
    {
        var names = new string?[Depth + 1];
        for (var i = 0; i <= Depth; i++)
        {
            names[i] = GetPropertyName(i);
        }

        return string.Join('.', names);
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
        Text,
    }

    internal struct Segment(SegmentKind kind, int start, int length)
    {
        public readonly SegmentKind Kind = kind;
        public readonly int Start = start;
        public readonly int Length = length;
        public string? Decoded;
    }
}
