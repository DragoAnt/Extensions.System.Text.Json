using System.Buffers;
using DragoAnt.System.Text.Json.Observer.Strategies;

namespace DragoAnt.System.Text.Json.Observer;

internal static class TagMasking
{
    /// <summary>
    /// Writes the strategy's replacement for the current value and moves past it; a container is never read.
    /// </summary>
    public static void Mask(ref Utf8JsonReader reader, JsonWriter writer, MaskTag tag, ref PropertyPath propPath)
    {
        var options = writer.Options;
        var strategy = options.MaskStrategy ?? Utf8MaskStrategy.Default;
        var tokenType = reader.TokenType;
        switch (tokenType)
        {
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                strategy.Mask(default, tokenType, tag, writer, options);
                if (!reader.TrySkip())
                {
                    propPath.Stop();
                }

                return;
            case JsonTokenType.Null:
                strategy.Mask(default, tokenType, tag, writer, options);
                return;
            case JsonTokenType.String when reader.HasValueSequence || reader.ValueIsEscaped:
                var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
                var buffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    var written = reader.CopyString(buffer);
                    strategy.Mask(buffer.AsSpan(0, written), tokenType, tag, writer, options);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                }

                return;
            default:
                strategy.Mask(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan, tokenType, tag, writer, options);
                return;
        }
    }
}
