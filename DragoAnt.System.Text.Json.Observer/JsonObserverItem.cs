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
                case Comment:
                    writer.WriteCommentValue(reader.GetComment());
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

    /// <summary>
    /// Apply masking policy for value string.
    /// </summary>
    /// <param name="maskingRule">Property masking rule.</param>
    /// <param name="valuePolicy">Value masking policy.</param>
    public static JsonObserverDelegate<TContext> ApplyValueRawPolicy(Func<string?, TContext, string?> maskingRule, JsonObserverValueDelegate<TContext>? valuePolicy)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int _,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            var effective = valuePolicy ?? defaultValuePolicy;
            if (reader.TokenType is
                not JsonTokenType.String and
                not Number and
                not True and
                not False and
                not Null)
            {
                effective.Invoke(ref reader, writer, context, ref propPath);
                return;
            }

            var val = GetRawStringValue(ref reader);
            var result = maskingRule(val, context);
            if (result is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(result);
            }
        };

        static string GetRawStringValue(ref Utf8JsonReader reader)
        {
            // Get the raw UTF-8 bytes for the current token
            var rawBytes = reader.HasValueSequence
                ? reader.ValueSequence.ToArray()
                : reader.ValueSpan;

            // Convert the bytes to a UTF-8 string
            return Encoding.UTF8.GetString(rawBytes);
        }
    }

    public static JsonObserverDelegate<TContext> ReadStr(Action<string?, TContext> read, JsonObserverValueDelegate<TContext>? valuePolicy) =>
        ApplyReadPolicy(
            (ref Utf8JsonReader reader, TContext context) => read(reader.TokenType is Null ? null : reader.GetString(), context),
            static type => type is JsonTokenType.String or Null,
            valuePolicy);

    public static JsonObserverDelegate<TContext> ReadInt(Action<int?, TContext> read, JsonObserverValueDelegate<TContext>? valuePolicy) =>
        ApplyReadPolicy(
            (ref Utf8JsonReader reader, TContext context) => read(reader.TokenType is Null ? null : reader.GetInt32(), context),
            static type => type is Number or Null,
            valuePolicy);

    public static JsonObserverDelegate<TContext> ReadLong(Action<long?, TContext> read, JsonObserverValueDelegate<TContext>? valuePolicy) =>
        ApplyReadPolicy(
            (ref Utf8JsonReader reader, TContext context) => read(reader.TokenType is Null ? null : reader.GetInt64(), context),
            static type => type is Number or Null,
            valuePolicy);

    public static JsonObserverDelegate<TContext> ReadDecimal(Action<decimal?, TContext> read, JsonObserverValueDelegate<TContext>? valuePolicy) =>
        ApplyReadPolicy(
            (ref Utf8JsonReader reader, TContext context) => read(reader.TokenType is Null ? null : reader.GetDecimal(), context),
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
            var tokenType = reader.TokenType;
            if (tokenType is Null)
            {
                writer.WriteNullValue();
                return;
            }

            var options = writer.Options;
            var strategy = options.MaskStrategy ?? Utf8MaskStrategy.Default;
            switch (tokenType)
            {
                case StartObject:
                case StartArray:
                    strategy.Mask(default, tokenType, tag, writer, options);
                    if (!reader.TrySkip())
                    {
                        propPath.Stop();
                    }

                    return;
                case JsonTokenType.String when reader.HasValueSequence || reader.ValueIsEscaped:
                    MaskEscapedString(ref reader, strategy, tag, writer, options);
                    return;
                default:
                    strategy.Mask(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan, tokenType, tag, writer, options);
                    return;
            }
        };

        static void MaskEscapedString(ref Utf8JsonReader reader, Utf8MaskStrategy strategy, MaskTag tag, JsonWriter writer, JsonObserverOptions options)
        {
            var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                var written = reader.CopyString(buffer);
                strategy.Mask(buffer.AsSpan(0, written), JsonTokenType.String, tag, writer, options);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }

    /// <summary>
    /// Masks a value of any JSON type; a container is skipped and reported to the rule as <c>null</c>.
    /// </summary>
    /// <param name="maskingRule">Property masking rule.</param>
    public static JsonObserverDelegate<TContext> ApplyAnyPolicy(Func<string?, TContext, string?> maskingRule)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int _,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> __) =>
        {
            string? value;
            switch (reader.TokenType)
            {
                case Null:
                    writer.WriteNullValue();
                    return;
                case StartObject:
                case StartArray:
                    value = null;
                    break;
                case JsonTokenType.String:
                    value = reader.GetString();
                    break;
                default:
                    value = Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan);
                    break;
            }

            var result = maskingRule(value, context);
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
    /// Apply masking policy for value string.
    /// </summary>
    /// <param name="maskingRule">Property masking rule.</param>
    /// <param name="valuePolicy">Value masking policy.</param>
    public static JsonObserverDelegate<TContext> ApplyValueStringPolicy(Func<string?, TContext, string?> maskingRule, JsonObserverValueDelegate<TContext>? valuePolicy)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int _,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            var effective = valuePolicy ?? defaultValuePolicy;
            if (reader.TokenType is not JsonTokenType.String and not Null)
            {
                effective.Invoke(ref reader, writer, context, ref propPath);
                return;
            }

            var val = reader.TokenType is Null ? null : reader.GetString();
            var result = maskingRule(val, context);
            if (result is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(result);
            }
        };
    }

    /// <summary>
    /// Apply masking policy for value <see cref="Int64"/>.
    /// </summary>
    /// <param name="maskingRule">Property masking rule.</param>
    /// <param name="valuePolicy">Value masking policy.</param>
    public static JsonObserverDelegate<TContext> ApplyValueBoolPolicy(Func<bool?, TContext, string?> maskingRule, JsonObserverValueDelegate<TContext>? valuePolicy)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int _,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            var effective = valuePolicy ?? defaultValuePolicy;
            if (reader.TokenType is not True and not False and not Null)
            {
                effective.Invoke(ref reader, writer, context, ref propPath);
                return;
            }

            var val = reader.TokenType is Null ? (bool?)null : reader.GetBoolean();
            var result = maskingRule(val, context);
            if (result is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(result);
            }
        };
    }

    /// <summary>
    /// Apply masking policy for value <see cref="Int64"/>.
    /// </summary>
    /// <param name="maskingRule">Property masking rule.</param>
    /// <param name="valuePolicy">Value masking policy.</param>
    public static JsonObserverDelegate<TContext> ApplyValueIntPolicy(Func<int?, TContext, string?> maskingRule, JsonObserverValueDelegate<TContext>? valuePolicy)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int _,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            var effective = valuePolicy ?? defaultValuePolicy;
            if (reader.TokenType is not Number and not Null)
            {
                effective.Invoke(ref reader, writer, context, ref propPath);
                return;
            }

            var val = reader.TokenType is Null ? (int?)null : reader.GetInt32();
            var result = maskingRule(val, context);
            if (result is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(result);
            }
        };
    }

    /// <summary>
    /// Apply masking policy for value <see cref="Int64"/>.
    /// </summary>
    /// <param name="maskingRule">Property masking rule.</param>
    /// <param name="valuePolicy">Value masking policy.</param>
    public static JsonObserverDelegate<TContext> ApplyValueLongPolicy(Func<long?, TContext, string?> maskingRule, JsonObserverValueDelegate<TContext>? valuePolicy)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int _,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            var effective = valuePolicy ?? defaultValuePolicy;
            if (reader.TokenType is not Number and not Null)
            {
                effective.Invoke(ref reader, writer, context, ref propPath);
                return;
            }

            var val = reader.TokenType is Null ? (long?)null : reader.GetInt64();
            var result = maskingRule(val, context);
            if (result is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(result);
            }
        };
    }

    /// <summary>
    /// Apply masking policy for value <see cref="Int64"/>.
    /// </summary>
    /// <param name="maskingRule">Property masking rule.</param>
    /// <param name="valuePolicy">Value masking policy.</param>
    public static JsonObserverDelegate<TContext> ApplyValueDecimalPolicy(
        Func<decimal?, TContext, string?> maskingRule,
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
            var effective = valuePolicy ?? defaultValuePolicy;
            if (reader.TokenType is not Number and not Null)
            {
                effective.Invoke(ref reader, writer, context, ref propPath);
                return;
            }

            var val = reader.TokenType is Null ? (decimal?)null : reader.GetDecimal();
            var result = maskingRule(val, context);
            if (result is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(result);
            }
        };
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
                case Comment:
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
                if (propPath.Stopped || !reader.Read())
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
                    case Comment:
                        writer.WriteCommentValue(reader.GetComment());
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
                if (propPath.Stopped || !reader.Read())
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
                    case Comment:
                        writer.WriteCommentValue(reader.GetComment());
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
                case Comment:
                    writer.WriteCommentValue(reader.GetComment());
                    break;
                case JsonTokenType.String:
                case Number:
                case True:
                case False:
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
