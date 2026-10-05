namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// State of one observer pass: the path, whether reading must stop, and the counters rules need.
/// </summary>
internal ref struct JsonWalk
{
    public DataPath Path;
    private readonly bool _hasInput;

    public JsonWalk(int capacity, ReadOnlySpan<byte> input, JsonObserverOptions options)
    {
        Path = new DataPath(input, capacity, options.NameCaseInsensitive);
        Options = options;
        _hasInput = !input.IsEmpty;
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

    public void Dispose() => Path.Dispose();
}
