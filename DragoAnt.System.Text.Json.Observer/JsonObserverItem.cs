using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using DragoAnt.System.Text.Json.Observer.Builders;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// JSON masking policy.
/// </summary>
/// <param name="propMatch">Property name matching delegate.</param>
/// <param name="masking">Masking policy delegate.</param>
internal sealed class JsonObserverItem<TContext>(JsonPropertyMatchDelegate propMatch, JsonObserverDelegate<TContext> masking)
{
    /// <summary>
    /// Any payload object or array.
    /// </summary>
    /// <param name="initObj">Init masking for object.</param>
    /// <param name="initArray">Init masking for array.</param>
    /// <param name="defaultValueMasking">Default policy for unknown scenarios.</param>
    public static JsonObserverDelegate<TContext> Any(
        Action<JsonObjBuilder<TContext>> initObj,
        Action<JsonArrayBuilder<TContext>> initArray,
        JsonObserverValueDelegate<TContext>? defaultValueMasking)
    {
        var objMasking = Obj(initObj, defaultValueMasking);
        var arrayMasking = Array(initArray, defaultValueMasking);

        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> valuePolicy) =>
        {
            switch (reader.TokenType)
            {
                case StartObject:
                    objMasking(ref reader, writer, context, depth, ref propPath, valuePolicy);
                    break;
                case StartArray:
                    arrayMasking(ref reader, writer, context, depth, ref propPath, valuePolicy);
                    break;
                case Null:
                    writer.WriteNullValue();
                    break;
                case JsonTokenType.String:
                case Number:
                case True:
                case False:
                case None:
                case EndObject:
                case EndArray:
                case PropertyName:
                default:
                    throw new JsonObserverException("Wrong path");
            }
        };
    }

    /// <summary>
    /// Object value masking policy.
    /// </summary>
    /// <param name="init">Init masking for object.</param>
    /// <param name="defaultValueMasking">Default masking for unknown scenarios.</param>
    public static JsonObserverDelegate<TContext> Obj(Action<JsonObjBuilder<TContext>> init, JsonObserverValueDelegate<TContext>? defaultValueMasking)
    {
        var builder = new JsonObjBuilder<TContext>(defaultValueMasking);
        init(builder);
        return JsonObjBuilder<TContext>.Build(builder);
    }

    /// <summary>
    /// Masking initialization for the array.
    /// </summary>
    /// <param name="init">Masking condition builder.</param>
    /// <param name="defaultValuePolicy">Default masking policy.</param>
    public static JsonObserverDelegate<TContext> Array(Action<JsonArrayBuilder<TContext>> init, JsonObserverValueDelegate<TContext>? defaultValuePolicy)
    {
        var builder = new JsonArrayBuilder<TContext>(defaultValuePolicy);
        init(builder);
        return JsonArrayBuilder<TContext>.Build(builder);
    }

    public static JsonObserverDelegate<TContext> ReadStr(Action<string?, TContext> read, JsonObserverValueDelegate<TContext>? valuePolicy) =>
        ApplyReadPolicy(
            (ref Utf8JsonReader reader, TContext context) => read(reader.TokenType is Null ? null : reader.GetString(), context),
            static type => type is JsonTokenType.String or Null,
            valuePolicy);

    public static JsonObserverDelegate<TContext> ReadInt(Action<int?, TContext> read, JsonObserverValueDelegate<TContext>? valuePolicy) =>
        ApplyReadPolicy(
            (ref Utf8JsonReader reader, TContext context) => read(reader.TokenType is Number && reader.TryGetInt32(out var v) ? v : null, context),
            static type => type is Number or Null,
            valuePolicy);

    public static JsonObserverDelegate<TContext> ReadLong(Action<long?, TContext> read, JsonObserverValueDelegate<TContext>? valuePolicy) =>
        ApplyReadPolicy(
            (ref Utf8JsonReader reader, TContext context) => read(reader.TokenType is Number && reader.TryGetInt64(out var v) ? v : null, context),
            static type => type is Number or Null,
            valuePolicy);

    public static JsonObserverDelegate<TContext> ReadDecimal(Action<decimal?, TContext> read, JsonObserverValueDelegate<TContext>? valuePolicy) =>
        ApplyReadPolicy(
            (ref Utf8JsonReader reader, TContext context) => read(reader.TokenType is Number && reader.TryGetDecimal(out var v) ? v : null, context),
            static type => type is Number or Null,
            valuePolicy);

    public static JsonObserverDelegate<TContext> ReadBool(Action<bool?, TContext> read, JsonObserverValueDelegate<TContext>? valuePolicy) =>
        ApplyReadPolicy(
            (ref Utf8JsonReader reader, TContext context) => read(reader.TokenType is Null ? null : reader.GetBoolean(), context),
            static type => type is True or False or Null,
            valuePolicy);

    public static JsonObserverDelegate<TContext> ReadRaw(Action<string?, TContext> read, JsonObserverValueDelegate<TContext>? valuePolicy) =>
        ApplyReadPolicy(
            (ref Utf8JsonReader reader, TContext context) =>
                read(Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan), context),
            static type => type is JsonTokenType.String or Number or True or False or Null,
            valuePolicy);

    private delegate void ReadValue(ref Utf8JsonReader reader, TContext context);

    /// <summary>
    /// Hands the value to <paramref name="read"/> and writes the token back unchanged.
    /// </summary>
    private static JsonObserverDelegate<TContext> ApplyReadPolicy(
        ReadValue read,
        Func<JsonTokenType, bool> accepts,
        JsonObserverValueDelegate<TContext>? valuePolicy)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int _,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            var tokenType = reader.TokenType;
            if (!accepts(tokenType))
            {
                (valuePolicy ?? defaultValuePolicy).Invoke(ref reader, writer, context, ref propPath);
                return;
            }

            read(ref reader, context);
            switch (tokenType)
            {
                case Null:
                    writer.WriteNullValue();
                    break;
                case JsonTokenType.String:
                    writer.CopyStringValue(ref reader);
                    break;
                case True:
                case False:
                    writer.WriteBooleanValue(tokenType is True);
                    break;
                default:
                    writer.CopyRawValue(ref reader);
                    break;
            }
        };
    }

    /// <summary>
    /// Masks a value of any JSON type with the call's <see cref="Utf8MaskStrategy"/>; a container is skipped.
    /// </summary>
    /// <param name="tag">Tag handed to the strategy.</param>
    public static JsonObserverDelegate<TContext> ApplyTagPolicy(MaskTag tag)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext _,
            int __,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> ___) =>
        {
            if (reader.TokenType is Null)
            {
                writer.WriteNullValue();
                return;
            }

            TagMasking.Mask(ref reader, writer, tag, ref propPath);
        };
    }

    /// <summary>
    /// Masks a value of any JSON type: a string arrives decoded, a number or boolean as its literal, an object or array
    /// is skipped unread and arrives as <c>null</c>; a <c>null</c> value stays <c>null</c>.
    /// </summary>
    public static JsonObserverDelegate<TContext> ApplyAnyPolicy(Func<string?, TContext, string?> maskingRule, string? constant = null) =>
        ApplyMaskPolicy(
            constant is not null
                ? (ref Utf8JsonReader _, TContext _, int _) => constant
                : (ref Utf8JsonReader reader, TContext context, int maxBytes) => maskingRule(ScalarText(ref reader, maxBytes, decode: true), context),
            keepNull: true);

    /// <summary>
    /// Like <see cref="ApplyAnyPolicy"/>, but the function is also called for <c>null</c>.
    /// </summary>
    public static JsonObserverDelegate<TContext> ApplyStringPolicy(Func<string?, TContext, string?> maskingRule, string? constant = null) =>
        ApplyMaskPolicy(
            constant is not null
                ? (ref Utf8JsonReader _, TContext _, int _) => constant
                : (ref Utf8JsonReader reader, TContext context, int maxBytes) => maskingRule(ScalarText(ref reader, maxBytes, decode: true), context),
            keepNull: false);

    /// <summary>
    /// Like <see cref="ApplyStringPolicy"/>, but a string arrives as its raw, still escaped JSON text.
    /// </summary>
    public static JsonObserverDelegate<TContext> ApplyRawPolicy(Func<string?, TContext, string?> maskingRule) =>
        ApplyMaskPolicy(
            (ref Utf8JsonReader reader, TContext context, int maxBytes) => maskingRule(ScalarText(ref reader, maxBytes, decode: false), context),
            keepNull: false);

    /// <summary>
    /// Masks a value of any JSON type; the function receives <c>true</c> or <c>false</c>, and <c>null</c> for anything else.
    /// </summary>
    public static JsonObserverDelegate<TContext> ApplyBoolPolicy(Func<bool?, TContext, string?> maskingRule) =>
        ApplyMaskPolicy(
            (ref Utf8JsonReader reader, TContext context, int _) => maskingRule(reader.TokenType switch
            {
                True => true,
                False => false,
                _ => null,
            }, context),
            keepNull: false);

    /// <summary>
    /// Masks a value of any JSON type; the function receives a number that fits <see cref="int"/>, and <c>null</c> for anything else.
    /// </summary>
    public static JsonObserverDelegate<TContext> ApplyIntPolicy(Func<int?, TContext, string?> maskingRule) =>
        ApplyMaskPolicy(
            (ref Utf8JsonReader reader, TContext context, int _) =>
                maskingRule(reader.TokenType is Number && reader.TryGetInt32(out var value) ? value : null, context),
            keepNull: false);

    /// <summary>
    /// Masks a value of any JSON type; the function receives a number that fits <see cref="long"/>, and <c>null</c> for anything else.
    /// </summary>
    public static JsonObserverDelegate<TContext> ApplyLongPolicy(Func<long?, TContext, string?> maskingRule) =>
        ApplyMaskPolicy(
            (ref Utf8JsonReader reader, TContext context, int _) =>
                maskingRule(reader.TokenType is Number && reader.TryGetInt64(out var value) ? value : null, context),
            keepNull: false);

    /// <summary>
    /// Masks a value of any JSON type; the function receives a number that fits <see cref="decimal"/>, and <c>null</c> for anything else.
    /// </summary>
    public static JsonObserverDelegate<TContext> ApplyDecimalPolicy(Func<decimal?, TContext, string?> maskingRule) =>
        ApplyMaskPolicy(
            (ref Utf8JsonReader reader, TContext context, int _) =>
                maskingRule(reader.TokenType is Number && reader.TryGetDecimal(out var value) ? value : null, context),
            keepNull: false);

    private delegate string? MaskToken(ref Utf8JsonReader reader, TContext context, int maxValueBytes);

    /// <summary>
    /// Writes the function's replacement for the current value whatever its type, then moves past it; a container is never read.
    /// </summary>
    private static JsonObserverDelegate<TContext> ApplyMaskPolicy(MaskToken mask, bool keepNull)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int _,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> __) =>
        {
            if (keepNull && reader.TokenType is Null)
            {
                writer.WriteNullValue();
                return;
            }

            var result = mask(ref reader, context, writer.Options.MaxValueBytes);
            if (result is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(result);
            }

            if (reader.TokenType is StartObject or StartArray && !reader.TrySkip())
            {
                propPath.Stop();
            }
        };
    }

    /// <summary>
    /// Text of a string, number or boolean token, at most <paramref name="maxBytes"/> UTF-8 bytes of it; <c>null</c> for anything else.
    /// </summary>
    private static string? ScalarText(ref Utf8JsonReader reader, int maxBytes, bool decode)
    {
        if (reader.TokenType is not (JsonTokenType.String or Number or True or False))
        {
            return null;
        }

        if (decode && reader.TokenType is JsonTokenType.String && (reader.ValueIsEscaped || reader.HasValueSequence))
        {
            var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                return Utf8Prefix(buffer.AsSpan(0, reader.CopyString(buffer)), maxBytes);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        if (!reader.HasValueSequence)
        {
            return Utf8Prefix(reader.ValueSpan, maxBytes);
        }

        var sequence = reader.ValueSequence;
        return Utf8Prefix(sequence.Slice(0, Math.Min(sequence.Length, (long)maxBytes + 4)).ToArray(), maxBytes);
    }

    private static string Utf8Prefix(ReadOnlySpan<byte> utf8, int maxBytes)
    {
        if (utf8.Length > maxBytes)
        {
            var cut = Math.Max(maxBytes, 0);
            while (cut > 0 && (utf8[cut] & 0xC0) == 0x80)
            {
                cut--;
            }

            utf8 = utf8[..cut];
        }

        return Encoding.UTF8.GetString(utf8);
    }

    /// <summary>
    /// Apply masking policy for values.
    /// </summary>
    /// <param name="policies">Property masking policies.</param>
    /// <param name="valuePolicy">Value masking delegate.</param>
    public static JsonObserverDelegate<TContext> ApplyValuePolicy(JsonObserverItem<TContext>[] policies, JsonObserverValueDelegate<TContext>? valuePolicy)
    {
        var defaultPolicy = GetApplyDefaultPolicy(valuePolicy, UnknownContainers.Create(policies));

        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                case Number:
                case True:
                case False:
                case Null:
                    var tokenType = reader.TokenType;
                    var (matchPolicy, nextDepth) = MatchPolicy(policies, depth, ref propPath, tokenType);
                    if (matchPolicy is not null)
                    {
                        matchPolicy.Apply(ref reader, writer, context, nextDepth, ref propPath, defaultValuePolicy);
                    }
                    else
                    {
                        defaultPolicy(ref reader, writer, context, nextDepth, ref propPath, defaultValuePolicy);
                    }
                    break;
                case EndArray:
                case StartObject:
                case StartArray:
                case PropertyName:
                case None:
                case EndObject:
                default:
                    throw new JsonObserverException("Wrong path");
            }
        };
    }

    internal static JsonObserverDelegate<TContext> ApplyObjPolicy(JsonObserverItem<TContext>[] policies, JsonObserverValueDelegate<TContext>? valuePolicy)
        => ApplyObjPolicy(policies, valuePolicy, UnknownContainers.Create(policies));

    internal static JsonObserverDelegate<TContext> ApplyArrayPolicy(JsonObserverItem<TContext>[] policies, JsonObserverValueDelegate<TContext>? valuePolicy)
        => ApplyArrayPolicy(policies, valuePolicy, UnknownContainers.Create(policies));

    private static JsonObserverDelegate<TContext> ApplyObjPolicy(
        JsonObserverItem<TContext>[] policies,
        JsonObserverValueDelegate<TContext>? valuePolicy,
        UnknownContainers unknown)
    {
        var defaultPolicy = GetApplyDefaultPolicy(valuePolicy, unknown);

        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            var effective = valuePolicy ?? defaultValuePolicy;

            if (reader.TokenType != StartObject)
            {
                throw new JsonObserverException("Wrong path");
            }

            RuntimeHelpers.EnsureSufficientExecutionStack();
            writer.WriteStartObject();

            while (true)
            {
                if (propPath.Stopped || writer.Stopped || !reader.Read())
                {
                    propPath.Stop();
                    return;
                }

                switch (reader.TokenType)
                {
                    case PropertyName:
                        propPath.AddPropertyName(ref reader);

                        if (!reader.Read())
                        {
                            propPath.RemovePropertyName();
                            propPath.Stop();
                            return;
                        }

                        var tokenType = reader.TokenType;

                        var (matchPolicy, nextDepth) = MatchPolicy(policies, depth, ref propPath, tokenType);

                        writer.WritePropertyName(propPath.CurrentUtf8);
                        if (matchPolicy is not null)
                        {
                            matchPolicy.Apply(ref reader, writer, context, nextDepth, ref propPath, effective);
                        }
                        else
                        {
                            defaultPolicy(ref reader, writer, context, nextDepth, ref propPath, effective);
                        }

                        propPath.RemovePropertyName();

                        break;
                    case EndObject:
                        writer.WriteEndObject();
                        return;
                    case None:
                    case StartObject:
                    case StartArray:
                    case EndArray:
                    case JsonTokenType.String:
                    case Number:
                    case True:
                    case False:
                    case Null:
                    default:
                        throw new JsonObserverException("Wrong path");
                }
            }
        };
    }

    private static JsonObserverDelegate<TContext> ApplyArrayPolicy(
        JsonObserverItem<TContext>[] policies,
        JsonObserverValueDelegate<TContext>? valuePolicy,
        UnknownContainers unknown)
    {
        var defaultPolicy = GetApplyDefaultPolicy(valuePolicy, unknown);

        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            var effective = valuePolicy ?? defaultValuePolicy;
            if (reader.TokenType != StartArray)
            {
                throw new JsonObserverException("Wrong path");
            }

            RuntimeHelpers.EnsureSufficientExecutionStack();
            writer.WriteStartArray();

            while (true)
            {
                if (propPath.Stopped || writer.Stopped || !reader.Read())
                {
                    propPath.Stop();
                    return;
                }

                switch (reader.TokenType)
                {
                    case StartObject:
                    case StartArray:
                    case JsonTokenType.String:
                    case Number:
                    case True:
                    case False:
                    case Null:
                        var tokenType = reader.TokenType;

                        propPath.AddPropertyName(null);
                        var (matchPolicy, nextDepth) = MatchPolicy(policies, depth, ref propPath, tokenType);

                        if (matchPolicy is not null)
                        {
                            matchPolicy.Apply(ref reader, writer, context, nextDepth, ref propPath, effective);
                        }
                        else
                        {
                            defaultPolicy(ref reader, writer, context, nextDepth, ref propPath, effective);
                        }
                        propPath.RemovePropertyName();
                        break;
                    case EndArray:
                        writer.WriteEndArray();
                        return;
                    case PropertyName:
                    case None:
                    case EndObject:
                    default:
                        throw new JsonObserverException("Wrong path");
                }
            }
        };
    }

    private static JsonObserverDelegate<TContext> GetApplyDefaultPolicy(JsonObserverValueDelegate<TContext>? valuePolicy, UnknownContainers unknown)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            var effective = valuePolicy ?? defaultValuePolicy;
            switch (reader.TokenType)
            {
                case StartObject:
                    if (effective.Target is RelativeValuePolicy<TContext> relativeObj && relativeObj.TryApplyContainer(ref reader, writer, context, ref propPath))
                    {
                        break;
                    }

                    unknown.Obj(ref reader, writer, context, depth, ref propPath, effective);
                    break;
                case StartArray:
                    if (effective.Target is RelativeValuePolicy<TContext> relativeArray && relativeArray.TryApplyContainer(ref reader, writer, context, ref propPath))
                    {
                        break;
                    }

                    unknown.Array(ref reader, writer, context, depth, ref propPath, effective);
                    break;
                case JsonTokenType.String:
                case Number:
                case True:
                case False:
                    effective(ref reader, writer, context, ref propPath);
                    break;
                case Null when effective.Target is RelativeValuePolicy<TContext>:
                    effective(ref reader, writer, context, ref propPath);
                    break;
                case Null:
                    writer.WriteNullValue();
                    break;
                case PropertyName:
                case EndObject:
                case EndArray:
                case None:
                default:
                    throw new JsonObserverException("Wrong path");
            }
        };
    }

    internal static (JsonObserverItem<TContext>?, int depth) MatchPolicy(
        JsonObserverItem<TContext>[] policies,
        int depth,
        ref PropertyPath path,
        JsonTokenType tokenType)
    {
        foreach (var policyItem in policies)
        {
            var (success, nextDepth) = policyItem.Match(depth, ref path, tokenType);
            if (!success)
            {
                continue;
            }

            return (policyItem, nextDepth + depth);
        }

        return (null, depth);
    }

    private (bool success, int depth) Match(int depth, ref PropertyPath propPath, JsonTokenType token) => propMatch(depth, ref propPath, token);

    internal void Apply(
        ref Utf8JsonReader reader,
        JsonWriter writer,
        TContext context,
        int depth,
        ref PropertyPath propPath,
        JsonObserverValueDelegate<TContext> defaultValue) =>
        masking(ref reader, writer, context, depth, ref propPath, defaultValue);

    /// <summary>
    /// Policies for containers no rule matched, built once so that no delegate mutates captured state at call time.
    /// </summary>
    private sealed class UnknownContainers
    {
        public JsonObserverDelegate<TContext> Obj { get; private set; } = null!;
        public JsonObserverDelegate<TContext> Array { get; private set; } = null!;

        public static UnknownContainers Create(JsonObserverItem<TContext>[] policies)
        {
            var unknown = new UnknownContainers();
            unknown.Obj = ApplyObjPolicy(policies, null, unknown);
            unknown.Array = ApplyArrayPolicy(policies, null, unknown);
            return unknown;
        }
    }
}
