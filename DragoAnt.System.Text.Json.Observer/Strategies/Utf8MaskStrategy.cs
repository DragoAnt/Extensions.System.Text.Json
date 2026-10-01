using System.Security.Cryptography;

namespace DragoAnt.System.Text.Json.Observer.Strategies;

/// <summary>
/// Masks a sensitive value given as UTF-8. One instance serves every rule: the rule's <see cref="MaskTag"/> says how.
/// </summary>
public abstract class Utf8MaskStrategy
{
    /// <summary>
    /// Built-in strategy: <see cref="MaskKind.Full"/>, <see cref="MaskKind.Last4"/>, <see cref="MaskKind.Hash"/>
    /// (HMAC-SHA256 with <see cref="JsonObserverOptions.HashKey"/>) and <see cref="MaskKind.Omit"/>.
    /// </summary>
    public static Utf8MaskStrategy Default { get; } = new DefaultUtf8MaskStrategy();

    /// <summary>
    /// Writes the masked replacement of one value.
    /// </summary>
    /// <param name="value">
    /// The unescaped text of a string, the literal of a number or boolean, or empty for an object or array.
    /// </param>
    /// <param name="tokenType">JSON type of the value; <see cref="JsonTokenType.StartObject"/> or <see cref="JsonTokenType.StartArray"/> for a container.</param>
    /// <param name="tag">How the rule asks for the value to be masked.</param>
    /// <param name="writer">Receives exactly one value.</param>
    /// <param name="options">Options of the current call.</param>
    public abstract void Mask(ReadOnlySpan<byte> value, JsonTokenType tokenType, MaskTag tag, JsonWriter writer, JsonObserverOptions options);

    private sealed class DefaultUtf8MaskStrategy : Utf8MaskStrategy
    {
        private const int Last4MinLength = 8;
        private const int HashHexLength = 16;
        private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);

        private static ReadOnlySpan<byte> Stars => "***"u8;
        private static ReadOnlySpan<byte> HashPrefix => "hash:"u8;
        private static ReadOnlySpan<byte> Hex => "0123456789abcdef"u8;

        public override void Mask(ReadOnlySpan<byte> value, JsonTokenType tokenType, MaskTag tag, JsonWriter writer, JsonObserverOptions options)
        {
            var scalar = tokenType is JsonTokenType.String or JsonTokenType.Number;
            switch (tag.Kind)
            {
                case MaskKind.Omit:
                    writer.WriteNullValue();
                    break;
                case MaskKind.Last4 when scalar:
                    WriteLast4(value, writer);
                    break;
                case MaskKind.Hash when scalar:
                    WriteHash(value, options.HashKey.IsEmpty ? ProcessKey : options.HashKey.Span, writer);
                    break;
                default:
                    writer.WriteStringValue(Stars);
                    break;
            }
        }

        private static void WriteLast4(ReadOnlySpan<byte> value, JsonWriter writer)
        {
            var start = value.Length;
            var chars = 0;
            var total = 0;
            for (var i = value.Length - 1; i >= 0; i--)
            {
                if ((value[i] & 0xC0) == 0x80)
                {
                    continue;
                }

                total++;
                if (chars < 4)
                {
                    chars++;
                    start = i;
                }
            }

            if (total < Last4MinLength)
            {
                writer.WriteStringValue(Stars);
                return;
            }

            var tail = value[start..];
            Span<byte> masked = stackalloc byte[Stars.Length + tail.Length];
            Stars.CopyTo(masked);
            tail.CopyTo(masked[Stars.Length..]);
            writer.WriteStringValue(masked);
        }

        private static void WriteHash(ReadOnlySpan<byte> value, ReadOnlySpan<byte> key, JsonWriter writer)
        {
            Span<byte> hash = stackalloc byte[32];
            HMACSHA256.HashData(key, value, hash);

            Span<byte> text = stackalloc byte[HashPrefix.Length + HashHexLength];
            HashPrefix.CopyTo(text);
            for (var i = 0; i < HashHexLength / 2; i++)
            {
                text[HashPrefix.Length + 2 * i] = Hex[hash[i] >> 4];
                text[HashPrefix.Length + 2 * i + 1] = Hex[hash[i] & 0xF];
            }

            writer.WriteStringValue(text);
        }
    }
}
