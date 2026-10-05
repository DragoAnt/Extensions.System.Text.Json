using System.Buffers;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// State of one observer pass: the path, whether reading must stop, and the counters rules need.
/// </summary>
internal ref struct JsonWalk
{
    public DataPath Path;
    private readonly ReadOnlySpan<byte> _input;
    private readonly ReadOnlySequence<byte> _sequence;
    private readonly bool _hasInput;

    public JsonWalk(int capacity, ReadOnlySpan<byte> input, JsonObserverOptions options)
    {
        Path = new DataPath(input, capacity, options.NameCaseInsensitive);
        Options = options;
        _input = input;
        _hasInput = !input.IsEmpty;
    }

    public JsonWalk(int capacity, ReadOnlySpan<byte> input, scoped in ReadOnlySequence<byte> sequence, JsonObserverOptions options, CommentPolicy? comments)
        : this(capacity, input, options)
    {
        _sequence = sequence;
        Comments = comments;
        Pending = comments is null ? null : CommentBuffer.Rent();
    }

    /// <summary>
    /// The comment policy of a pass that writes comments; <c>null</c> when the reader skips them.
    /// </summary>
    public CommentPolicy? Comments { get; }

    public CommentBuffer? Pending { get; }

    /// <summary>
    /// Input offset just after the last value written, to tell an inline comment from one on a later line.
    /// </summary>
    public long LastValueEnd { get; set; }

    public readonly bool IsSameLine(long from, long to)
    {
        if (to <= from)
        {
            return true;
        }

        if (!_input.IsEmpty)
        {
            return _input.Slice((int)from, (int)(to - from)).IndexOfAny((byte)'\n', (byte)'\r') < 0;
        }

        var slice = _sequence.Slice(from, to - from);
        return slice.PositionOf((byte)'\n') is null && slice.PositionOf((byte)'\r') is null;
    }

    public readonly CommentStyle StyleAt(long tokenStart)
    {
        var at = tokenStart + 1;
        byte marker;
        if (!_input.IsEmpty)
        {
            marker = at < _input.Length ? _input[(int)at] : (byte)'*';
        }
        else
        {
            marker = at < _sequence.Length ? _sequence.Slice(at, 1).FirstSpan[0] : (byte)'*';
        }

        return marker == (byte)'/' ? CommentStyle.Line : CommentStyle.Block;
    }

    public JsonObserverOptions Options { get; }

    /// <summary>
    /// The input ended inside a value, or a rule asked to stop: every rule must stop reading.
    /// </summary>
    public bool Stopped { get; private set; }

    /// <summary>
    /// Position of the next value handed to a strategy.
    /// </summary>
    public long ValueIndex { get; set; }

    public readonly int CurrentDepth => Path.Length - 1;

    public readonly int MaxLength => Path.MaxLength;

    public readonly ReadOnlySpan<byte> CurrentUtf8 => Path.LastName;

    public void Stop() => Stopped = true;

    /// <summary>
    /// Adds the property name the reader stands on, pointing into the input when it is one span and unescaped.
    /// </summary>
    public void AddPropertyName(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence && !reader.ValueIsEscaped && _hasInput)
        {
            Path.PushInputName(checked((int)reader.TokenStartIndex + 1), reader.ValueSpan.Length);
            return;
        }

        var maxLength = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        var written = reader.CopyString(Path.ReserveName(maxLength));
        Path.PushReservedName(written);
    }

    public void AddPropertyName(ReadOnlySpan<byte> utf8Name) => Path.PushName(utf8Name);

    public void AddArrayItem(int index) => Path.PushItem(index);

    public void RemovePropertyName() => Path.Pop();

    public void Dispose()
    {
        Path.Dispose();
        Pending?.Return();
    }
}
