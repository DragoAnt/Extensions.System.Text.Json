using System.Buffers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
#if NET10_0_OR_GREATER
using REDox.Json;
using RedoxJsonDocument = REDox.Json.JsonDocument;
using RedoxJsonValueKind = REDox.Json.JsonValueKind;
#endif

namespace DragoAnt.System.Text.Json.Observer.Benchmarks;

public static partial class Baselines
{
    public const string Mask = "***";

    private static readonly HashSet<string> SensitiveSet = new(Payloads.SensitiveNames, StringComparer.OrdinalIgnoreCase);
    private static readonly byte[][] SensitiveUtf8 = Payloads.SensitiveNames.Select(Encoding.UTF8.GetBytes).ToArray();

    public static JsonObserver BuildObserver(int ruleCount = 8, string[]? names = null)
    {
        names ??= Payloads.SensitiveNames;
        var count = Math.Min(ruleCount, names.Length);
        var policy = JsonValuePolicy.AnyDepth(b =>
            {
                for (var i = 0; i < count; i++)
                {
                    b.Match(names[i]).Mask((_, _) => Mask, MaskNulls.Mask);
                }
            },
            ValuePolicy.BlockList);
        return JsonObserver.Any(o => { }, a => { }, policy);
    }

    public static JsonObserver<TContext> BuildObserver<TContext>(Action<Builders.JsonAnyDepthBuilder<TContext>> extra)
    {
        var policy = JsonValuePolicy.AnyDepth<TContext>(b =>
            {
                foreach (var name in Payloads.SensitiveNames)
                {
                    b.Match(name).Mask((_, _) => Mask, MaskNulls.Mask);
                }

                extra(b);
            },
            ValuePolicy.BlockList);
        return JsonObserver.Any<TContext>(o => { }, a => { }, policy);
    }

    public static string CopyString(string json)
    {
        var maxBytes = Encoding.UTF8.GetMaxByteCount(json.Length);
        var rented = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            var len = Encoding.UTF8.GetBytes(json, rented);
            var output = new ArrayBufferWriter<byte>(len + 64);
            using (var writer = new Utf8JsonWriter(output))
            {
                var reader = new Utf8JsonReader(rented.AsSpan(0, len));
                Copy(ref reader, writer, maskNames: false);
            }

            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static void CopyBytes(ReadOnlySpan<byte> json, IBufferWriter<byte> output, Utf8JsonWriter writer, bool maskNames)
    {
        writer.Reset(output);
        var reader = new Utf8JsonReader(json);
        Copy(ref reader, writer, maskNames);
        writer.Flush();
    }

    public static string SpanMaskString(string json)
    {
        var maxBytes = Encoding.UTF8.GetMaxByteCount(json.Length);
        var rented = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            var len = Encoding.UTF8.GetBytes(json, rented);
            var output = new ArrayBufferWriter<byte>(len + 64);
            using (var writer = new Utf8JsonWriter(output))
            {
                var reader = new Utf8JsonReader(rented.AsSpan(0, len));
                Copy(ref reader, writer, maskNames: true);
            }

            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void Copy(ref Utf8JsonReader reader, Utf8JsonWriter writer, bool maskNames)
    {
        var maskNext = false;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    maskNext = false;
                    writer.WriteStartObject();
                    break;
                case JsonTokenType.EndObject:
                    writer.WriteEndObject();
                    break;
                case JsonTokenType.StartArray:
                    maskNext = false;
                    writer.WriteStartArray();
                    break;
                case JsonTokenType.EndArray:
                    writer.WriteEndArray();
                    break;
                case JsonTokenType.PropertyName:
                    if (reader.ValueIsEscaped)
                    {
                        var name = reader.GetString()!;
                        maskNext = maskNames && SensitiveSet.Contains(name);
                        writer.WritePropertyName(name);
                    }
                    else
                    {
                        maskNext = maskNames && IsSensitive(reader.ValueSpan);
                        writer.WritePropertyName(reader.ValueSpan);
                    }

                    break;
                case JsonTokenType.String:
                    if (maskNext)
                    {
                        writer.WriteStringValue(Mask);
                    }
                    else if (reader.ValueIsEscaped)
                    {
                        writer.WriteStringValue(reader.GetString());
                    }
                    else
                    {
                        writer.WriteStringValue(reader.ValueSpan);
                    }

                    maskNext = false;
                    break;
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    if (maskNext)
                    {
                        writer.WriteStringValue(Mask);
                    }
                    else
                    {
                        writer.WriteRawValue(reader.ValueSpan, skipInputValidation: true);
                    }

                    maskNext = false;
                    break;
            }
        }
    }

    private static bool IsSensitive(ReadOnlySpan<byte> name)
    {
        foreach (var candidate in SensitiveUtf8)
        {
            if (name.Length == candidate.Length && AsciiEqualsIgnoreCase(name, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AsciiEqualsIgnoreCase(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        for (var i = 0; i < a.Length; i++)
        {
            if ((a[i] | 0x20) != (b[i] | 0x20))
            {
                return false;
            }
        }

        return true;
    }

    public static string Dom(string json)
    {
        var node = JsonNode.Parse(json)!;
        MaskNode(node);
        return node.ToJsonString();
    }

#if NET10_0_OR_GREATER
    private static readonly REDox.Json.JsonDocumentOptions RedoxOptions = new()
    {
        EnableValueValidation = true,
        MaxDepth = 64,
    };

    public static ExtractContext RedoxExtract(ReadOnlyMemory<byte> json)
    {
        using var doc = RedoxJsonDocument.Parse(json, options: RedoxOptions);
        var root = doc.RootElement;
        return new ExtractContext
        {
            Id = root.GetProperty("id").GetString(),
            Type = root.GetProperty("type").GetString(),
            CustomerId = root.GetProperty("customer").GetProperty("customerId").GetInt32(),
        };
    }

    public static string RedoxMask(string json)
    {
        using var doc = RedoxJsonDocument.Parse(json, options: RedoxOptions);
        MaskRedox(doc.RootElement);
        return RedoxJsonDocument.EncodeToString(doc.RootElement);
    }

    public static void RedoxMask(ReadOnlyMemory<byte> json, IBufferWriter<byte> output)
    {
        using var doc = RedoxJsonDocument.Parse(json, options: RedoxOptions);
        MaskRedox(doc.RootElement);
        RedoxJsonDocument.EncodeTo(doc.RootElement, output);
    }

    private static void MaskRedox(REDox.DElement element)
    {
        switch (element.ValueKind)
        {
            case RedoxJsonValueKind.Object:
                var obj = element.AsObject();
                for (var i = 0; i < obj.Count; i++)
                {
                    var property = obj.GetAt(i);
                    if (SensitiveSet.Contains(property.Key))
                    {
                        obj[i] = Mask;
                    }
                    else
                    {
                        MaskRedox(property.Value.AsElement());
                    }
                }

                break;
            case RedoxJsonValueKind.Array:
                var array = element.AsArray();
                for (var i = 0; i < array.Count; i++)
                {
                    MaskRedox(array[i].AsElement());
                }

                break;
        }
    }

#endif

    private static void MaskNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                List<string>? toMask = null;
                foreach (var (key, value) in obj)
                {
                    if (SensitiveSet.Contains(key))
                    {
                        (toMask ??= []).Add(key);
                    }
                    else if (value is not null)
                    {
                        MaskNode(value);
                    }
                }

                if (toMask is not null)
                {
                    foreach (var key in toMask)
                    {
                        obj[key] = Mask;
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        MaskNode(item);
                    }
                }

                break;
        }
    }

    public static string Regex(string json) => SensitiveRegex().Replace(json, "\"$1\":\"" + Mask + "\"");

    [GeneratedRegex("\"(password|cardNumber|cvv|email|phone|accessToken|secret|iban)\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveRegex();
}
