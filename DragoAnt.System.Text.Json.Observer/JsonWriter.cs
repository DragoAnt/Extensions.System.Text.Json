using System.Buffers;
using System.Text;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Output of an observer pass. A custom rule writes exactly one value for the token it is given.
/// </summary>
public abstract class JsonWriter
{
    private const int StackallocThreshold = 256;

    internal static readonly JsonWriter Empty = new EmptyJsonWriter();

    private protected JsonWriter()
    {
    }

    /// <summary>
    /// Writes <c>null</c>.
    /// </summary>
    public abstract void WriteNullValue();

    /// <summary>
    /// Writes <c>true</c> or <c>false</c>.
    /// </summary>
    /// <param name="value">Value to write.</param>
    public abstract void WriteBooleanValue(bool value);

    /// <summary>
    /// Writes a string value; <c>null</c> is written as <c>null</c>.
    /// </summary>
    /// <param name="value">Value to write.</param>
    public abstract void WriteStringValue(string? value);

    /// <summary>
    /// Writes a number.
    /// </summary>
    /// <param name="value">Value to write.</param>
    public abstract void WriteNumberValue(long value);

    /// <summary>
    /// Writes a number.
    /// </summary>
    /// <param name="value">Value to write.</param>
    public abstract void WriteNumberValue(decimal value);

    /// <summary>
    /// Writes a property name; the value written next belongs to it.
    /// </summary>
    /// <param name="propertyName">Name to write.</param>
    public abstract void WritePropertyName(string propertyName);

    /// <summary>
    /// Opens an object.
    /// </summary>
    public abstract void WriteStartObject();

    /// <summary>
    /// Closes the innermost open object.
    /// </summary>
    public abstract void WriteEndObject();

    /// <summary>
    /// Opens an array.
    /// </summary>
    public abstract void WriteStartArray();

    /// <summary>
    /// Closes the innermost open array.
    /// </summary>
    public abstract void WriteEndArray();

    /// <summary>
    /// Writes a property name given as unescaped UTF-8 text.
    /// </summary>
    /// <param name="utf8PropertyName">Unescaped UTF-8 name.</param>
    public abstract void WritePropertyName(ReadOnlySpan<byte> utf8PropertyName);

    /// <summary>
    /// Writes a string value given as unescaped UTF-8 text.
    /// </summary>
    /// <param name="utf8Value">Unescaped UTF-8 value.</param>
    public abstract void WriteStringValue(ReadOnlySpan<byte> utf8Value);

    /// <summary>
    /// Writes an already valid JSON value as is, for example a number literal.
    /// </summary>
    /// <param name="utf8Json">UTF-8 JSON value.</param>
    public abstract void WriteRawValue(ReadOnlySpan<byte> utf8Json);

    /// <summary>
    /// Writes a string value given as UTF-16 text, for example the output of a char-based redactor, without a
    /// <see cref="string"/> allocation.
    /// </summary>
    /// <param name="value">Unescaped text; an empty span writes <c>""</c>.</param>
    public abstract void WriteStringValue(ReadOnlySpan<char> value);

    /// <summary>
    /// Writes a property name given as UTF-16 text; the value written next belongs to it.
    /// </summary>
    /// <param name="propertyName">Unescaped name.</param>
    public abstract void WritePropertyName(ReadOnlySpan<char> propertyName);

    /// <summary>
    /// Writes bytes as a Base64 string value, for example a hash or an encrypted value.
    /// </summary>
    /// <param name="bytes">Bytes to encode.</param>
    public abstract void WriteBase64StringValue(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Writes a number; <see cref="double.NaN"/> and infinities, which JSON cannot represent, are written as strings.
    /// </summary>
    /// <param name="value">Value to write.</param>
    public abstract void WriteNumberValue(double value);

    /// <summary>
    /// Options of the current call; rules with a <see cref="MaskTag"/> read their strategy and hash key here.
    /// </summary>
    internal virtual JsonObserverOptions Options => JsonObserverOptions.Default;

    /// <summary>
    /// The output is full: nothing more is written, so reading can stop.
    /// </summary>
    internal virtual bool Stopped => false;

    /// <summary>
    /// The strings written now replace a value, so <see cref="ObserverOptions.MaxValueBytes"/> does not cut them.
    /// </summary>
    internal virtual bool MaskOutput
    {
        get => false;
        set { }
    }

    /// <summary>
    /// Writes a comment with the given UTF-8 text, for the comment policy; a writer that has no comments ignores it.
    /// </summary>
    internal virtual void WriteComment(ReadOnlySpan<byte> utf8Text)
    {
    }

    internal void CopyStringValue(ref Utf8JsonReader reader)
    {
        if (ReferenceEquals(this, Empty))
        {
            return;
        }

        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            WriteStringValue(reader.ValueSpan);
            return;
        }

        var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        byte[]? rented = null;
        var buffer = length <= StackallocThreshold
            ? stackalloc byte[StackallocThreshold]
            : rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var written = reader.CopyString(buffer);
            WriteStringValue(buffer[..written]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
    }

    internal void CopyRawValue(ref Utf8JsonReader reader)
    {
        if (ReferenceEquals(this, Empty))
        {
            return;
        }

        if (!reader.HasValueSequence)
        {
            WriteRawValue(reader.ValueSpan);
            return;
        }

        var length = checked((int)reader.ValueSequence.Length);
        byte[]? rented = null;
        var buffer = length <= StackallocThreshold
            ? stackalloc byte[StackallocThreshold]
            : rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            reader.ValueSequence.CopyTo(buffer);
            WriteRawValue(buffer[..length]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
    }

    private sealed class EmptyJsonWriter : JsonWriter
    {
        public override void WriteNullValue()
        {
        }

        public override void WriteBooleanValue(bool value)
        {
        }

        public override void WriteStringValue(string? value)
        {
        }

        public override void WriteNumberValue(long value)
        {
        }

        public override void WriteNumberValue(decimal value)
        {
        }

        public override void WritePropertyName(string propertyName)
        {
        }

        public override void WritePropertyName(ReadOnlySpan<byte> utf8PropertyName)
        {
        }

        public override void WriteStringValue(ReadOnlySpan<byte> utf8Value)
        {
        }

        public override void WriteRawValue(ReadOnlySpan<byte> utf8Json)
        {
        }

        public override void WriteStringValue(ReadOnlySpan<char> value)
        {
        }

        public override void WritePropertyName(ReadOnlySpan<char> propertyName)
        {
        }

        public override void WriteBase64StringValue(ReadOnlySpan<byte> bytes)
        {
        }

        public override void WriteNumberValue(double value)
        {
        }

        public override void WriteStartObject()
        {
        }

        public override void WriteEndObject()
        {
        }

        public override void WriteStartArray()
        {
        }

        public override void WriteEndArray()
        {
        }
    }
}

/// <summary>
/// Drops <c>null</c> values: a property name and an opened container are written only once a non-null value follows.
/// </summary>
internal sealed class IgnoreNullsJsonWriter : JsonWriter, IDisposable
{
    [ThreadStatic]
    private static IgnoreNullsJsonWriter? t_cached;

    private JsonWriter _inner = Empty;
    private Pending[] _pending = [];
    private byte[] _names = [];
    private int _count;
    private int _namesUsed;

    internal override JsonObserverOptions Options => _inner.Options;

    internal override bool Stopped => _inner.Stopped;

    internal override bool MaskOutput
    {
        get => _inner.MaskOutput;
        set => _inner.MaskOutput = value;
    }

    public override void WriteNullValue()
    {
        if (_count > 0 && _pending[_count - 1].Kind == Kind.Name)
        {
            Pop();
        }
        else if (_count == 0 || !_pending[_count - 1].IsArray)
        {
            Flush();
            _inner.WriteNullValue();
        }
    }

    public override void WriteBooleanValue(bool value)
    {
        Flush();
        _inner.WriteBooleanValue(value);
    }

    public override void WriteStringValue(string? value)
    {
        if (value is null)
        {
            WriteNullValue();
            return;
        }

        Flush();
        _inner.WriteStringValue(value);
    }

    public override void WriteStringValue(ReadOnlySpan<byte> utf8Value)
    {
        Flush();
        _inner.WriteStringValue(utf8Value);
    }

    public override void WriteRawValue(ReadOnlySpan<byte> utf8Json)
    {
        Flush();
        _inner.WriteRawValue(utf8Json);
    }

    public override void WriteNumberValue(long value)
    {
        Flush();
        _inner.WriteNumberValue(value);
    }

    public override void WriteNumberValue(decimal value)
    {
        Flush();
        _inner.WriteNumberValue(value);
    }

    public override void WriteStringValue(ReadOnlySpan<char> value)
    {
        Flush();
        _inner.WriteStringValue(value);
    }

    public override void WriteBase64StringValue(ReadOnlySpan<byte> bytes)
    {
        Flush();
        _inner.WriteBase64StringValue(bytes);
    }

    internal override void WriteComment(ReadOnlySpan<byte> utf8Text)
    {
        Flush();
        _inner.WriteComment(utf8Text);
    }

    public override void WriteNumberValue(double value)
    {
        Flush();
        _inner.WriteNumberValue(value);
    }

    public override void WritePropertyName(string propertyName) => WritePropertyName(propertyName.AsSpan());

    public override void WritePropertyName(ReadOnlySpan<char> propertyName)
    {
        EnsureNames(Encoding.UTF8.GetMaxByteCount(propertyName.Length));
        var written = Encoding.UTF8.GetBytes(propertyName, _names.AsSpan(_namesUsed));
        Push(new Pending(Kind.Name, _namesUsed, written, false));
        _namesUsed += written;
    }

    public override void WritePropertyName(ReadOnlySpan<byte> utf8PropertyName)
    {
        EnsureNames(utf8PropertyName.Length);
        utf8PropertyName.CopyTo(_names.AsSpan(_namesUsed));
        Push(new Pending(Kind.Name, _namesUsed, utf8PropertyName.Length, false));
        _namesUsed += utf8PropertyName.Length;
    }

    private void EnsureNames(int length)
    {
        if (_namesUsed + length <= _names.Length)
        {
            return;
        }

        var grown = ArrayPool<byte>.Shared.Rent(Math.Max(_names.Length * 2, _namesUsed + length));
        _names.AsSpan(0, _namesUsed).CopyTo(grown);
        ArrayPool<byte>.Shared.Return(_names, clearArray: true);
        _names = grown;
    }

    public override void WriteStartObject() => Push(new Pending(Kind.Open, 0, 0, false));

    public override void WriteStartArray() => Push(new Pending(Kind.Open, 0, 0, true));

    public override void WriteEndObject() => End(isArray: false);

    public override void WriteEndArray() => End(isArray: true);

    /// <summary>
    /// Takes this thread's spare writer, or creates one, writing to <paramref name="inner"/>; <see cref="Dispose"/> gives it back.
    /// </summary>
    public static IgnoreNullsJsonWriter Rent(JsonWriter inner)
    {
        var writer = t_cached ?? new IgnoreNullsJsonWriter();
        t_cached = null;
        writer._inner = inner;
        writer._pending = ArrayPool<Pending>.Shared.Rent(16);
        writer._names = ArrayPool<byte>.Shared.Rent(256);
        writer._count = 0;
        writer._namesUsed = 0;
        return writer;
    }

    /// <summary>
    /// Returns the pooled buffers and keeps the writer as this thread's spare.
    /// </summary>
    public void Dispose()
    {
        ArrayPool<Pending>.Shared.Return(_pending);
        ArrayPool<byte>.Shared.Return(_names, clearArray: true);
        _pending = [];
        _names = [];
        _inner = Empty;
        t_cached = this;
    }

    private void End(bool isArray)
    {
        if (_count > 0 && _pending[_count - 1].Kind == Kind.Open)
        {
            Pop();
            if (_count > 0 && _pending[_count - 1].Kind == Kind.Name)
            {
                Pop();
            }
            else if (_count == 0)
            {
                WriteEmptyRoot(isArray);
            }

            return;
        }

        if (isArray)
        {
            _inner.WriteEndArray();
        }
        else
        {
            _inner.WriteEndObject();
        }

        if (_count > 0 && _pending[_count - 1].Kind == Kind.Written)
        {
            _count--;
        }
    }

    private void WriteEmptyRoot(bool isArray)
    {
        if (isArray)
        {
            _inner.WriteStartArray();
            _inner.WriteEndArray();
        }
        else
        {
            _inner.WriteStartObject();
            _inner.WriteEndObject();
        }
    }

    /// <summary>
    /// Writes every pending name and opened container, because a non-null value follows.
    /// </summary>
    private void Flush()
    {
        for (var i = 0; i < _count; i++)
        {
            ref var pending = ref _pending[i];
            switch (pending.Kind)
            {
                case Kind.Name:
                    _inner.WritePropertyName(_names.AsSpan(pending.Start, pending.Length));
                    break;
                case Kind.Open when pending.IsArray:
                    _inner.WriteStartArray();
                    pending = pending with { Kind = Kind.Written };
                    continue;
                case Kind.Open:
                    _inner.WriteStartObject();
                    pending = pending with { Kind = Kind.Written };
                    continue;
                default:
                    continue;
            }

            pending = pending with { Kind = Kind.Flushed };
        }

        _namesUsed = 0;
        Compact();
    }

    private void Compact()
    {
        var kept = 0;
        for (var i = 0; i < _count; i++)
        {
            if (_pending[i].Kind != Kind.Flushed)
            {
                _pending[kept++] = _pending[i];
            }
        }

        _count = kept;
    }

    private void Push(Pending pending)
    {
        if (_count == _pending.Length)
        {
            var grown = ArrayPool<Pending>.Shared.Rent(_count * 2);
            _pending.AsSpan(0, _count).CopyTo(grown);
            ArrayPool<Pending>.Shared.Return(_pending);
            _pending = grown;
        }

        _pending[_count++] = pending;
    }

    private void Pop()
    {
        var pending = _pending[--_count];
        if (pending.Kind == Kind.Name)
        {
            _namesUsed = pending.Start;
        }
    }

    private enum Kind : byte
    {
        Name,
        Open,
        Written,
        Flushed,
    }

    private readonly record struct Pending(Kind Kind, int Start, int Length, bool IsArray);
}
