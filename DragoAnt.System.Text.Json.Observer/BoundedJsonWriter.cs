using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Writer for the bytes API: buffers the output so that it can be cut back to the last complete value and closed,
/// either when it reaches its size limit or when reading stops early.
/// </summary>
internal sealed class BoundedJsonWriter : JsonWriter, IDisposable
{
    [ThreadStatic]
    private static BoundedJsonWriter? t_cached;

    private static ReadOnlySpan<byte> Ellipsis => [0xE2, 0x80, 0xA6];

    private readonly PooledBufferWriter _buffer;
    private readonly Utf8JsonWriter _writer;
    private readonly bool _relaxedEscaping;
    private readonly bool _indented;
    private readonly int _writerMaxDepth;
    private int _maxOutputBytes;
    private int _maxValueBytes;
    private bool[] _isArray = [];
    private int _depth;
    private int _safeLength;
    private int _safeDepth;

    private BoundedJsonWriter(JsonObserverOptions options)
    {
        _relaxedEscaping = options.RelaxedEscaping;
        _indented = options.Indented;
        _writerMaxDepth = WriterMaxDepth(options);
        _buffer = new PooledBufferWriter();
        _writer = new Utf8JsonWriter(_buffer, new JsonWriterOptions
        {
            Encoder = options.RelaxedEscaping ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping : null,
            MaxDepth = _writerMaxDepth,
            Indented = options.Indented,
        });
        Start(options);
    }

    public bool Exhausted { get; private set; }

    /// <summary>
    /// At least one string value was cut to <see cref="JsonObserverOptions.MaxValueBytes"/>.
    /// </summary>
    public bool ValuesTruncated { get; private set; }

    internal override JsonObserverOptions Options => _options;

    private JsonObserverOptions _options = null!;

    /// <summary>
    /// Takes this thread's spare writer when its fixed settings fit <paramref name="options"/>, or creates one;
    /// <see cref="Dispose"/> gives it back. A nested call on the same thread gets a writer of its own.
    /// </summary>
    public static BoundedJsonWriter Rent(JsonObserverOptions options)
    {
        var cached = t_cached;
        if (cached is null || cached._relaxedEscaping != options.RelaxedEscaping || cached._indented != options.Indented ||
            cached._writerMaxDepth != WriterMaxDepth(options))
        {
            return new BoundedJsonWriter(options);
        }

        t_cached = null;
        cached._buffer.Reset();
        cached._writer.Reset(cached._buffer);
        cached.Start(options);
        return cached;
    }

    private static int WriterMaxDepth(JsonObserverOptions options) => Math.Min(Math.Max(options.MaxDepth, 1), int.MaxValue - 1) + 1;

    private void Start(JsonObserverOptions options)
    {
        _options = options;
        _maxOutputBytes = Math.Max(options.MaxOutputBytes, 0);
        _maxValueBytes = Math.Max(options.MaxValueBytes, 0);
        _isArray = ArrayPool<bool>.Shared.Rent(64);
        _depth = 0;
        _safeLength = 0;
        _safeDepth = 0;
        Exhausted = false;
        ValuesTruncated = false;
    }

    internal override bool Stopped => Exhausted;

    private int Length => checked((int)(_writer.BytesCommitted + _writer.BytesPending));

    public override void WriteNullValue()
    {
        if (Exhausted)
        {
            return;
        }

        _writer.WriteNullValue();
        Completed();
    }

    public override void WriteBooleanValue(bool value)
    {
        if (Exhausted)
        {
            return;
        }

        _writer.WriteBooleanValue(value);
        Completed();
    }

    public override void WriteStringValue(string? value)
    {
        if (Exhausted)
        {
            return;
        }

        if (value is null)
        {
            WriteNullValue();
            return;
        }

        WriteStringValue(value.AsSpan());
    }

    public override void WriteStringValue(ReadOnlySpan<char> value)
    {
        if (Exhausted)
        {
            return;
        }

        if ((long)value.Length * 3 <= _maxValueBytes)
        {
            _writer.WriteStringValue(value);
            Completed();
            return;
        }

        var chars = value[..(int)Math.Min(value.Length, (long)_maxValueBytes + 1)];
        if (chars.Length < value.Length && char.IsHighSurrogate(chars[^1]))
        {
            chars = chars[..^1];
        }

        var encoded = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(chars.Length));
        try
        {
            var utf8 = encoded.AsSpan(0, Encoding.UTF8.GetBytes(chars, encoded));
            if (chars.Length < value.Length && utf8.Length <= _maxValueBytes)
            {
                WriteCut(utf8, utf8.Length);
            }
            else
            {
                WriteStringValue(utf8);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(encoded, clearArray: true);
        }
    }

    public override void WriteStringValue(ReadOnlySpan<byte> utf8Value)
    {
        if (Exhausted)
        {
            return;
        }

        if (utf8Value.Length <= _maxValueBytes)
        {
            _writer.WriteStringValue(utf8Value);
            Completed();
            return;
        }

        var cut = _maxValueBytes;
        while (cut > 0 && (utf8Value[cut] & 0xC0) == 0x80)
        {
            cut--;
        }

        WriteCut(utf8Value, cut);
    }

    private void WriteCut(ReadOnlySpan<byte> utf8Value, int cut)
    {
        ValuesTruncated = true;
        var shortened = ArrayPool<byte>.Shared.Rent(cut + Ellipsis.Length);
        try
        {
            utf8Value[..cut].CopyTo(shortened);
            Ellipsis.CopyTo(shortened.AsSpan(cut));
            _writer.WriteStringValue(shortened.AsSpan(0, cut + Ellipsis.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(shortened);
        }

        Completed();
    }

    public override void WriteRawValue(ReadOnlySpan<byte> utf8Json)
    {
        if (Exhausted)
        {
            return;
        }

        _writer.WriteRawValue(utf8Json, skipInputValidation: true);
        Completed();
    }

    public override void WriteNumberValue(long value)
    {
        if (Exhausted)
        {
            return;
        }

        _writer.WriteNumberValue(value);
        Completed();
    }

    public override void WriteNumberValue(decimal value)
    {
        if (Exhausted)
        {
            return;
        }

        _writer.WriteNumberValue(value);
        Completed();
    }

    public override void WriteNumberValue(double value)
    {
        if (Exhausted)
        {
            return;
        }

        if (!double.IsFinite(value))
        {
            Span<char> text = stackalloc char[16];
            value.TryFormat(text, out var length, provider: CultureInfo.InvariantCulture);
            WriteStringValue(text[..length]);
            return;
        }

        _writer.WriteNumberValue(value);
        Completed();
    }

    public override void WriteBase64StringValue(ReadOnlySpan<byte> bytes)
    {
        if (Exhausted)
        {
            return;
        }

        var length = Base64.GetMaxEncodedToUtf8Length(bytes.Length);
        if (length <= _maxValueBytes)
        {
            _writer.WriteBase64StringValue(bytes);
            Completed();
            return;
        }

        var encoded = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            Base64.EncodeToUtf8(bytes, encoded, out _, out var written);
            WriteStringValue(encoded.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(encoded, clearArray: true);
        }
    }

    public override void WritePropertyName(string propertyName)
    {
        if (!Exhausted)
        {
            _writer.WritePropertyName(propertyName);
        }
    }

    public override void WritePropertyName(ReadOnlySpan<char> propertyName)
    {
        if (!Exhausted)
        {
            _writer.WritePropertyName(propertyName);
        }
    }

    public override void WritePropertyName(ReadOnlySpan<byte> utf8PropertyName)
    {
        if (!Exhausted)
        {
            _writer.WritePropertyName(utf8PropertyName);
        }
    }

    public override void WriteStartObject() => Start(isArray: false);

    public override void WriteStartArray() => Start(isArray: true);

    public override void WriteEndObject() => End(isArray: false);

    public override void WriteEndArray() => End(isArray: true);

    /// <summary>
    /// Copies the output. A partial output is cut back to the last complete value and its open containers are closed.
    /// </summary>
    public int CopyTo(IBufferWriter<byte> output, bool complete)
    {
        _writer.Flush();
        if (complete && !Exhausted)
        {
            output.Write(_buffer.WrittenSpan);
            return _buffer.WrittenCount;
        }

        var length = _safeLength + _safeDepth;
        var target = output.GetSpan(Math.Max(length, 1));
        _buffer.WrittenSpan[.._safeLength].CopyTo(target);
        for (var level = _safeDepth - 1; level >= 0; level--)
        {
            target[length - level - 1] = _isArray[level] ? (byte)']' : (byte)'}';
        }

        output.Advance(length);
        return length;
    }

    /// <summary>
    /// Returns the pooled buffers and keeps the writer as this thread's spare.
    /// </summary>
    public void Dispose()
    {
        _writer.Reset(_buffer);
        _buffer.Dispose();
        var isArray = _isArray;
        _isArray = [];
        if (isArray.Length > 0)
        {
            ArrayPool<bool>.Shared.Return(isArray);
        }

        _options = JsonObserverOptions.Default;
        t_cached = this;
    }

    private void Start(bool isArray)
    {
        if (Exhausted)
        {
            return;
        }

        if (_depth == _isArray.Length)
        {
            var grown = ArrayPool<bool>.Shared.Rent(_depth * 2);
            _isArray.AsSpan().CopyTo(grown);
            ArrayPool<bool>.Shared.Return(_isArray);
            _isArray = grown;
        }

        if (isArray)
        {
            _writer.WriteStartArray();
        }
        else
        {
            _writer.WriteStartObject();
        }

        _isArray[_depth++] = isArray;
        Completed();
    }

    private void End(bool isArray)
    {
        if (Exhausted)
        {
            return;
        }

        if (isArray)
        {
            _writer.WriteEndArray();
        }
        else
        {
            _writer.WriteEndObject();
        }

        _depth--;
        Completed();
    }

    /// <summary>
    /// A value (or a container boundary) is fully written: keep it if it and the closing brackets still fit.
    /// </summary>
    private void Completed()
    {
        var length = Length;
        if (length + _depth > _maxOutputBytes)
        {
            Exhausted = true;
            return;
        }

        _safeLength = length;
        _safeDepth = _depth;
    }
}
