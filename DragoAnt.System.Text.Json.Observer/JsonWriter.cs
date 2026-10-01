using System.Buffers;
using System.Text;
using static DragoAnt.System.Text.Json.Observer.JsonWriter.IgnoreNullsJsonTokenType;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Output of an observer pass: receives the masked tokens.
/// </summary>
public abstract class JsonWriter
{
    private const int StackallocThreshold = 256;

    /// <summary>
    /// Writer that discards everything; used when values are only read.
    /// </summary>
    public static readonly JsonWriter Empty = new EmptyJsonWriter();

    /// <summary>
    /// Wraps a <see cref="Utf8JsonWriter"/>.
    /// </summary>
    /// <param name="writer">Target writer.</param>
    /// <param name="ignoreNulls">Drop properties whose value is <c>null</c>, and containers left empty by that.</param>
    /// <param name="ignoreComments">Drop comments.</param>
    public static JsonWriter FromUtf8JsonWriter(Utf8JsonWriter writer, bool ignoreNulls = false, bool ignoreComments = false)
        => ignoreNulls
            ? new IgnoreNullsTextJsonWriter(writer, ignoreComments)
            : new TextJsonWriter(writer, ignoreComments);

    public abstract void WriteNullValue();
    public abstract void WriteBooleanValue(bool value);
    public abstract void WriteStringValue(string? value);
    public abstract void WriteNumberValue(long value);
    public abstract void WriteNumberValue(decimal value);
    public abstract void WriteCommentValue(string comment);
    public abstract void WritePropertyName(string propertyName);
    public abstract void WriteStartObject();
    public abstract void WriteEndObject();
    public abstract void WriteStartArray();
    public abstract void WriteEndArray();

    /// <summary>
    /// Writes a property name given as unescaped UTF-8 text.
    /// </summary>
    /// <param name="utf8PropertyName">Unescaped UTF-8 name.</param>
    public virtual void WritePropertyName(ReadOnlySpan<byte> utf8PropertyName) => WritePropertyName(Encoding.UTF8.GetString(utf8PropertyName));

    /// <summary>
    /// Writes a string value given as unescaped UTF-8 text.
    /// </summary>
    /// <param name="utf8Value">Unescaped UTF-8 value.</param>
    public virtual void WriteStringValue(ReadOnlySpan<byte> utf8Value) => WriteStringValue(Encoding.UTF8.GetString(utf8Value));

    /// <summary>
    /// Writes an already valid JSON value as is, for example a number literal.
    /// </summary>
    /// <param name="utf8Json">UTF-8 JSON value.</param>
    public virtual void WriteRawValue(ReadOnlySpan<byte> utf8Json) =>
        throw new NotSupportedException($"{GetType().Name} does not support raw values.");

    /// <summary>
    /// Options of the current call; rules with a <see cref="Strategies.MaskTag"/> read their strategy and hash key here.
    /// </summary>
    internal virtual JsonObserverOptions Options => JsonObserverOptions.Default;

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
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    internal void CopyRawValue(ref Utf8JsonReader reader)
    {
        if (ReferenceEquals(this, Empty))
        {
            return;
        }

        if (reader.HasValueSequence)
        {
            WriteRawValue(reader.ValueSequence.ToArray());
            return;
        }

        WriteRawValue(reader.ValueSpan);
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

        public override void WriteCommentValue(string comment)
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

    private sealed class TextJsonWriter(Utf8JsonWriter writer, bool ignoreComments) : JsonWriter
    {
        public override void WriteNullValue() => writer.WriteNullValue();
        public override void WriteBooleanValue(bool value) => writer.WriteBooleanValue(value);
        public override void WriteStringValue(string? value) => writer.WriteStringValue(value);
        public override void WriteStringValue(ReadOnlySpan<byte> utf8Value) => writer.WriteStringValue(utf8Value);
        public override void WriteRawValue(ReadOnlySpan<byte> utf8Json) => writer.WriteRawValue(utf8Json, skipInputValidation: true);
        public override void WriteNumberValue(long value) => writer.WriteNumberValue(value);
        public override void WriteNumberValue(decimal value) => writer.WriteNumberValue(value);

        public override void WriteCommentValue(string comment)
        {
            if (ignoreComments)
            {
                return;
            }

            writer.WriteCommentValue(comment);
        }

        public override void WritePropertyName(string propertyName) => writer.WritePropertyName(propertyName);
        public override void WritePropertyName(ReadOnlySpan<byte> utf8PropertyName) => writer.WritePropertyName(utf8PropertyName);
        public override void WriteStartObject() => writer.WriteStartObject();
        public override void WriteEndObject() => writer.WriteEndObject();
        public override void WriteStartArray() => writer.WriteStartArray();
        public override void WriteEndArray() => writer.WriteEndArray();
    }

    private sealed class IgnoreNullsTextJsonWriter(Utf8JsonWriter writer, bool ignoreComments = false) : JsonWriter
    {
        private readonly Stack<(IgnoreNullsJsonTokenType type, string? propName)> _stack = new();

        public override void WriteNullValue()
        {
            var item = _stack.Peek();
            switch (item.type)
            {
                case StartArr:
                case StartedArr:
                    return;
                case PropName:
                    _stack.Pop();
                    return;
                default: throw new InvalidOperationException("Unexpected type");
            }
        }

        public override void WriteBooleanValue(bool value)
        {
            StartGroupOnStack();
            writer.WriteBooleanValue(value);
        }

        public override void WriteStringValue(string? value)
        {
            StartGroupOnStack();
            writer.WriteStringValue(value);
        }

        public override void WriteStringValue(ReadOnlySpan<byte> utf8Value)
        {
            StartGroupOnStack();
            writer.WriteStringValue(utf8Value);
        }

        public override void WriteRawValue(ReadOnlySpan<byte> utf8Json)
        {
            StartGroupOnStack();
            writer.WriteRawValue(utf8Json, skipInputValidation: true);
        }

        public override void WriteNumberValue(long value)
        {
            StartGroupOnStack();
            writer.WriteNumberValue(value);
        }

        public override void WriteNumberValue(decimal value)
        {
            StartGroupOnStack();
            writer.WriteNumberValue(value);
        }

        public override void WriteCommentValue(string comment)
        {
            if (ignoreComments)
            {
                return;
            }

            StartGroupOnStack();
            writer.WriteCommentValue(comment);
        }

        public override void WritePropertyName(string propertyName) => _stack.Push((PropName, propertyName));
        public override void WriteStartObject() => _stack.Push((StartObj, null));

        public override void WriteEndObject()
        {
            var item = _stack.Pop();
            switch (item.type)
            {
                case StartObj:
                    if (_stack.TryPeek(out item) && item.type is PropName)
                    {
                        _stack.Pop();
                    }
                    return;
                case StartedObj:
                    StartGroupOnStack();
                    writer.WriteEndObject();
                    break;
                default: throw new InvalidOperationException("Unexpected type");
            }
        }

        public override void WriteStartArray() => _stack.Push((StartArr, null));

        public override void WriteEndArray()
        {
            var item = _stack.Pop();
            switch (item.type)
            {
                case StartArr:
                    if (_stack.TryPeek(out item) && item.type is PropName)
                    {
                        _stack.Pop();
                    }
                    return;
                case StartedArr:
                    StartGroupOnStack();
                    writer.WriteEndArray();
                    break;
                default: throw new InvalidOperationException("Unexpected type");
            }
        }

        private void StartGroupOnStack()
        {
            if (!_stack.TryPeek(out var item))
            {
                return;
            }

            switch (item.type)
            {
                case StartedObj:
                case StartedArr:
                    break;
                case StartObj:
                    _stack.Pop();
                    StartGroupOnStack();
                    writer.WriteStartObject();
                    _stack.Push((StartedObj, null));
                    return;
                case StartArr:
                    _stack.Pop();
                    StartGroupOnStack();
                    writer.WriteStartArray();
                    _stack.Push((StartedArr, null));
                    return;
                case PropName:
                    item = _stack.Pop();
                    StartGroupOnStack();
                    writer.WritePropertyName(item.propName!);
                    return;
                default: throw new InvalidOperationException("Unexpected type");
            }
        }
    }

    public enum IgnoreNullsJsonTokenType : byte
    {
        None,
        StartObj,
        StartedObj,
        StartArr,
        StartedArr,
        PropName,
    }
}
