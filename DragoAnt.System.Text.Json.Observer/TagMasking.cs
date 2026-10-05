using System.Buffers;
using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer;

internal static class TagMasking
{
    private const int StackallocThreshold = 256;

    /// <summary>
    /// Writes the call's strategy's replacement for the current value and moves past it; a container is not read
    /// unless the strategy keeps it.
    /// </summary>
    public static void Mask(ref Utf8JsonReader reader, JsonWriter writer, MaskTag tag, ref JsonWalk walk) =>
        Mask(ref reader, writer, tag, walk.Options.Strategy ?? ValueMaskStrategy.Default, ref walk);

    /// <summary>
    /// Writes <paramref name="strategy"/>'s replacement for the current value and moves past it.
    /// </summary>
    public static void Mask(ref Utf8JsonReader reader, JsonWriter writer, MaskTag tag, ValueMaskStrategy strategy, ref JsonWalk walk)
    {
        var output = JsonMaskValueWriter.Rent(writer);
        bool keep;
        bool failed;
        writer.MaskOutput = true;
        try
        {
            MaskValue(ref reader, output, tag, strategy, ref walk);
        }
        finally
        {
            writer.MaskOutput = false;
            keep = output.KeepRequested;
            failed = output.Failed;
            output.Return();
        }

        if (failed)
        {
            throw new JsonObserverException("The strategy wrote an invalid number.");
        }

        if (keep)
        {
            JsonCopy.CopyValue(ref reader, writer, ref walk);
        }
        else if (reader.TokenType is StartObject or StartArray && !reader.TrySkip())
        {
            walk.Stop();
        }
    }

    private static void MaskValue(ref Utf8JsonReader reader, JsonMaskValueWriter output, MaskTag tag, ValueMaskStrategy strategy, ref JsonWalk walk)
    {
        var options = walk.Options;
        var tokenType = reader.TokenType;
        var kind = KindOf(tokenType);
        var index = walk.ValueIndex++;
        switch (tokenType)
        {
            case StartObject:
            case StartArray:
            case Null:
                strategy.Mask(new MaskContext(default, kind, tag, options, in walk.Path, index), output);
                return;
            case JsonTokenType.String when reader.HasValueSequence || reader.ValueIsEscaped:
            {
                var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
                var buffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    var written = reader.CopyString(buffer);
                    strategy.Mask(new MaskContext(buffer.AsSpan(0, written), kind, tag, options, in walk.Path, index), output);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                }

                return;
            }
            case not JsonTokenType.String when reader.HasValueSequence:
            {
                var length = checked((int)reader.ValueSequence.Length);
                byte[]? rented = null;
                var buffer = length <= StackallocThreshold ? stackalloc byte[StackallocThreshold] : rented = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    reader.ValueSequence.CopyTo(buffer);
                    strategy.Mask(new MaskContext(buffer[..length], kind, tag, options, in walk.Path, index), output);
                }
                finally
                {
                    if (rented is not null)
                    {
                        ArrayPool<byte>.Shared.Return(rented, clearArray: true);
                    }
                }

                return;
            }
            default:
                strategy.Mask(new MaskContext(reader.ValueSpan, kind, tag, options, in walk.Path, index), output);
                return;
        }
    }

    public static ValueKind KindOf(JsonTokenType tokenType) => tokenType switch
    {
        JsonTokenType.String => ValueKind.String,
        Number => ValueKind.Number,
        True or False => ValueKind.Boolean,
        StartObject => ValueKind.Object,
        StartArray => ValueKind.Array,
        _ => ValueKind.Null,
    };
}

/// <summary>
/// The JSON <see cref="MaskValueWriter"/>: writes a strategy's replacement through a <see cref="JsonWriter"/>.
/// One instance per thread, reused across calls.
/// </summary>
internal sealed class JsonMaskValueWriter : MaskValueWriter
{
    [ThreadStatic]
    private static JsonMaskValueWriter? t_cached;

    private JsonWriter _writer = JsonWriter.Empty;

    public bool KeepRequested { get; private set; }

    public bool Failed { get; private set; }

    public static JsonMaskValueWriter Rent(JsonWriter writer)
    {
        var output = t_cached ?? new JsonMaskValueWriter();
        t_cached = null;
        output._writer = writer;
        output.KeepRequested = false;
        output.Failed = false;
        return output;
    }

    public void Return()
    {
        _writer = JsonWriter.Empty;
        t_cached = this;
    }

    public override void String(ReadOnlySpan<byte> utf8) => _writer.WriteStringValue(utf8);

    public override void String(ReadOnlySpan<char> chars) => _writer.WriteStringValue(chars);

    public override void Boolean(bool value) => _writer.WriteBooleanValue(value);

    public override void Null() => _writer.WriteNullValue();

    public override void Keep() => KeepRequested = true;

    public override void Comment(ReadOnlySpan<char> text)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(global::System.Text.Encoding.UTF8.GetMaxByteCount(text.Length));
        try
        {
            _writer.WriteComment(bytes.AsSpan(0, global::System.Text.Encoding.UTF8.GetBytes(text, bytes)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes, clearArray: true);
        }
    }

    protected override void WriteNumber(ReadOnlySpan<byte> utf8Literal) => _writer.WriteRawValue(utf8Literal);

    protected override void InvalidNumber() => Failed = true;
}

/// <summary>
/// Copies a value unchanged, an object or array with everything in it.
/// </summary>
internal static class JsonCopy
{
    public static void CopyValue(ref Utf8JsonReader reader, JsonWriter writer, ref JsonWalk walk)
    {
        switch (reader.TokenType)
        {
            case StartObject:
            case StartArray:
                CopyContainer(ref reader, writer, ref walk);
                return;
            default:
                BuiltInPolicies<object?>.BlockList(ref reader, writer, null, ref walk);
                return;
        }
    }

    private static void CopyContainer(ref Utf8JsonReader reader, JsonWriter writer, ref JsonWalk walk)
    {
        var depth = reader.CurrentDepth;
        if (reader.TokenType is StartObject)
        {
            writer.WriteStartObject();
        }
        else
        {
            writer.WriteStartArray();
        }

        while (true)
        {
            if (writer.Stopped || !reader.Read())
            {
                walk.Stop();
                return;
            }

            switch (reader.TokenType)
            {
                case EndObject:
                    writer.WriteEndObject();
                    if (reader.CurrentDepth == depth)
                    {
                        return;
                    }

                    break;
                case EndArray:
                    writer.WriteEndArray();
                    if (reader.CurrentDepth == depth)
                    {
                        return;
                    }

                    break;
                case StartObject:
                    writer.WriteStartObject();
                    break;
                case StartArray:
                    writer.WriteStartArray();
                    break;
                case PropertyName:
                    walk.AddPropertyName(ref reader);
                    writer.WritePropertyName(walk.CurrentUtf8);
                    walk.RemovePropertyName();
                    break;
                case Comment:
                    break;
                default:
                    BuiltInPolicies<object?>.BlockList(ref reader, writer, null, ref walk);
                    break;
            }
        }
    }
}
