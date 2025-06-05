using System.Buffers;
using System.Text;
using DragoAnt.System.Text.Json.Observer.Builders;
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
            valuePolicy ??= defaultValuePolicy;
            if (reader.TokenType is
                not JsonTokenType.String and
                not Number and
                not True and
                not False and
                not Null)
            {
                valuePolicy.Invoke(ref reader, writer, context, ref propPath);
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
            valuePolicy ??= defaultValuePolicy;
            if (reader.TokenType is not JsonTokenType.String and not Null)
            {
                valuePolicy.Invoke(ref reader, writer, context, ref propPath);
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
            valuePolicy ??= defaultValuePolicy;
            if (reader.TokenType is not True and not False and not Null)
            {
                valuePolicy.Invoke(ref reader, writer, context, ref propPath);
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
            valuePolicy ??= defaultValuePolicy;
            if (reader.TokenType is not Number and not Null)
            {
                valuePolicy.Invoke(ref reader, writer, context, ref propPath);
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
            valuePolicy ??= defaultValuePolicy;
            if (reader.TokenType is not Number and not Null)
            {
                valuePolicy.Invoke(ref reader, writer, context, ref propPath);
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
            valuePolicy ??= defaultValuePolicy;
            if (reader.TokenType is not Number and not Null)
            {
                valuePolicy.Invoke(ref reader, writer, context, ref propPath);
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
        var defaultPolicy = GetApplyDefaultPolicy(policies, valuePolicy);

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
                    var policyMethod = matchPolicy is not null ? matchPolicy.Apply : defaultPolicy;
                    policyMethod(ref reader, writer, context, nextDepth, ref propPath, defaultValuePolicy);
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
    {
        var defaultPolicy = GetApplyDefaultPolicy(policies, valuePolicy);

        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            valuePolicy ??= defaultValuePolicy;

            if (reader.TokenType != StartObject)
            {
                throw new JsonObserverException("Wrong path");
            }

            writer.WriteStartObject();

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case PropertyName:
                        var propertyName = reader.GetString()!;

                        reader.Read();
                        var tokenType = reader.TokenType;

                        propPath.AddPropertyName(propertyName);
                        var (matchPolicy, nextDepth) = MatchPolicy(policies, depth, ref propPath, tokenType);
                        var policyMethod = matchPolicy is not null ? matchPolicy.Apply : defaultPolicy;

                        writer.WritePropertyName(propertyName);
                        policyMethod(ref reader, writer, context, nextDepth, ref propPath, valuePolicy);

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

    internal static JsonObserverDelegate<TContext> ApplyArrayPolicy(JsonObserverItem<TContext>[] policies, JsonObserverValueDelegate<TContext>? valuePolicy)
    {
        var defaultPolicy = GetApplyDefaultPolicy(policies, valuePolicy);

        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            valuePolicy ??= defaultValuePolicy;
            if (reader.TokenType != StartArray)
            {
                throw new JsonObserverException("Wrong path");
            }

            writer.WriteStartArray();

            while (reader.Read())
            {
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
                        var policyMethod = matchPolicy is not null ? matchPolicy.Apply : defaultPolicy;

                        policyMethod(ref reader, writer, context, nextDepth, ref propPath, valuePolicy);
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

    private static JsonObserverDelegate<TContext> GetApplyDefaultPolicy(JsonObserverItem<TContext>[] policies, JsonObserverValueDelegate<TContext>? valuePolicy)
    {
        JsonObserverDelegate<TContext>? objPolicy = null;
        JsonObserverDelegate<TContext>? arrayPolicy = null;

        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref PropertyPath propPath,
            JsonObserverValueDelegate<TContext> defaultValuePolicy) =>
        {
            valuePolicy ??= defaultValuePolicy;
            switch (reader.TokenType)
            {
                case StartObject:
                    objPolicy ??= ApplyObjPolicy(policies, null);
                    objPolicy(ref reader, writer, context, depth, ref propPath, valuePolicy);
                    break;
                case StartArray:
                    arrayPolicy ??= ApplyArrayPolicy(policies, null);
                    arrayPolicy(ref reader, writer, context, depth, ref propPath, valuePolicy);
                    break;
                case Comment:
                    writer.WriteCommentValue(reader.GetComment());
                    break;
                case JsonTokenType.String:
                case Number:
                    defaultValuePolicy(ref reader, writer, context, ref propPath);
                    break;
                case True:
                    writer.WriteBooleanValue(true);
                    break;
                case False:
                    writer.WriteBooleanValue(false);
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

    private static (JsonObserverItem<TContext>?, int depth) MatchPolicy(
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

    private void Apply(
        ref Utf8JsonReader reader,
        JsonWriter writer,
        TContext context,
        int depth,
        ref PropertyPath propPath,
        JsonObserverValueDelegate<TContext> defaultValue) =>
        masking(ref reader, writer, context, depth, ref propPath, defaultValue);
}