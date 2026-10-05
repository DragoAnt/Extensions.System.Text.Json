using System.Buffers;
using DragoAnt.System.Text.Json.Observer.Strategies;

namespace DragoAnt.System.Text.Json.Observer;

internal static class TagMasking
{
    private const int StackallocThreshold = 256;

    /// <summary>
    /// Writes the strategy's replacement for the current value and moves past it; a container is never read.
    /// </summary>
    public static void Mask(ref Utf8JsonReader reader, JsonWriter writer, MaskTag tag, ref PropertyPath propPath)
    {
        writer.MaskOutput = true;
        try
        {
            MaskValue(ref reader, writer, tag, ref propPath);
        }
        finally
        {
            writer.MaskOutput = false;
        }
    }

    private static void MaskValue(ref Utf8JsonReader reader, JsonWriter writer, MaskTag tag, ref PropertyPath propPath)
    {
        var options = writer.Options;
        var strategy = options.MaskStrategy ?? Utf8MaskStrategy.Default;
        var tokenType = reader.TokenType;
        switch (tokenType)
        {
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                strategy.Mask(new Utf8MaskContext(default, tokenType, tag, options, propPath), writer);
                if (!reader.TrySkip())
                {
                    propPath.Stop();
                }

                return;
            case JsonTokenType.Null:
                strategy.Mask(new Utf8MaskContext(default, tokenType, tag, options, propPath), writer);
                return;
            case JsonTokenType.String when reader.HasValueSequence || reader.ValueIsEscaped:
            {
                var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
                var buffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    var written = reader.CopyString(buffer);
                    strategy.Mask(new Utf8MaskContext(buffer.AsSpan(0, written), tokenType, tag, options, propPath), writer);
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
                    strategy.Mask(new Utf8MaskContext(buffer[..length], tokenType, tag, options, propPath), writer);
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
                strategy.Mask(new Utf8MaskContext(reader.ValueSpan, tokenType, tag, options, propPath), writer);
                return;
        }
    }
}
