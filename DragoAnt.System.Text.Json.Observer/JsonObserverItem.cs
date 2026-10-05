using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using DragoAnt.System.Text.Json.Observer.Builders;
using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// JSON masking policy.
/// </summary>
/// <param name="propMatch">Property name matching delegate.</param>
/// <param name="masking">Masking policy delegate.</param>
internal sealed class JsonObserverItem<TContext>(JsonPropertyMatchDelegate propMatch, ObserveRule<TContext> masking)
{
    /// <summary>
    /// What the rule tests and does, for explanations.
    /// </summary>
    public RuleInfo<TContext> Info { get; init; } = RuleInfo<TContext>.Unknown;

    /// <summary>
    /// Placements whose comments <see cref="CommentRule"/> decides for the members this rule matches.
    /// </summary>
    public CommentKind CommentKinds { get; set; }

    /// <summary>
    /// The rule's comment rule, set with <c>.Comment(kinds, rule)</c>; <c>null</c> leaves comments to the call's policy.
    /// </summary>
    public CommentRule? CommentRule { get; set; }

    public CommentRule? CommentRuleFor(CommentKind kind) => (CommentKinds & kind) != 0 ? CommentRule : null;

    /// <summary>
    /// Any payload object or array.
    /// </summary>
    /// <param name="initObj">Init masking for object.</param>
    /// <param name="initArray">Init masking for array.</param>
    /// <param name="defaultValueMasking">Default policy for unknown scenarios.</param>
    public static (ObserveRule<TContext> Delegate, RuleSet<TContext> Obj, RuleSet<TContext> Array) Any(
        Action<JsonObjBuilder<TContext>> initObj,
        Action<JsonArrayBuilder<TContext>> initArray,
        JsonValuePolicy<TContext>? defaultValueMasking)
    {
        var (objMasking, objSet) = Obj(initObj, defaultValueMasking);
        var (arrayMasking, arraySet) = Array(initArray, defaultValueMasking);

        return ((
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref JsonWalk propPath,
            ValueRule<TContext> valuePolicy) =>
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
        }, objSet, arraySet);
    }

    /// <summary>
    /// Object value masking policy.
    /// </summary>
    /// <param name="init">Init masking for object.</param>
    /// <param name="defaultValueMasking">Default masking for unknown scenarios.</param>
    public static (ObserveRule<TContext> Delegate, RuleSet<TContext> Set) Obj(
        Action<JsonObjBuilder<TContext>> init,
        JsonValuePolicy<TContext>? defaultValueMasking)
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
    public static (ObserveRule<TContext> Delegate, RuleSet<TContext> Set) Array(
        Action<JsonArrayBuilder<TContext>> init,
        JsonValuePolicy<TContext>? defaultValuePolicy)
    {
        var builder = new JsonArrayBuilder<TContext>(defaultValuePolicy);
        init(builder);
        return JsonArrayBuilder<TContext>.Build(builder);
    }

    /// <summary>
    /// What the rule reads; <c>null</c> for a rule that writes the value.
    /// </summary>
    public ReadValue? Reader { get; init; }

    public static ReadValue ReadStr(Action<string?, TContext> read) =>
        (ref Utf8JsonReader reader, TContext context) =>
        {
            if (reader.TokenType is JsonTokenType.String or Null)
            {
                read(reader.TokenType is Null ? null : reader.GetString(), context);
            }
        };

    public static ReadValue ReadInt(Action<int?, TContext> read) =>
        (ref Utf8JsonReader reader, TContext context) =>
        {
            if (reader.TokenType is Number or Null)
            {
                read(reader.TokenType is Number && reader.TryGetInt32(out var v) ? v : null, context);
            }
        };

    public static ReadValue ReadLong(Action<long?, TContext> read) =>
        (ref Utf8JsonReader reader, TContext context) =>
        {
            if (reader.TokenType is Number or Null)
            {
                read(reader.TokenType is Number && reader.TryGetInt64(out var v) ? v : null, context);
            }
        };

    public static ReadValue ReadDecimal(Action<decimal?, TContext> read) =>
        (ref Utf8JsonReader reader, TContext context) =>
        {
            if (reader.TokenType is Number or Null)
            {
                read(reader.TokenType is Number && reader.TryGetDecimal(out var v) ? v : null, context);
            }
        };

    public static ReadValue ReadBool(Action<bool?, TContext> read) =>
        (ref Utf8JsonReader reader, TContext context) =>
        {
            if (reader.TokenType is True or False or Null)
            {
                read(reader.TokenType is Null ? null : reader.GetBoolean(), context);
            }
        };

    public static ReadValue ReadRaw(Action<string?, TContext> read) =>
        (ref Utf8JsonReader reader, TContext context) =>
        {
            if (reader.TokenType is JsonTokenType.String or Number or True or False or Null)
            {
                read(reader.HasValueSequence ? Encoding.UTF8.GetString(reader.ValueSequence) : Encoding.UTF8.GetString(reader.ValueSpan), context);
            }
        };

    internal delegate void ReadValue(ref Utf8JsonReader reader, TContext context);

    /// <summary>
    /// A read rule: hands the value to <paramref name="read"/> and leaves the writing to the next matching rule or the default policy.
    /// </summary>
    public static JsonObserverItem<TContext> Read(JsonPropertyMatchDelegate propMatch, ReadValue read, RuleInfo<TContext> info) =>
        new(propMatch, WriteByDefault) { Reader = read, Info = info };

    private static void WriteByDefault(
        ref Utf8JsonReader reader,
        JsonWriter writer,
        TContext context,
        int _,
        ref JsonWalk propPath,
        ValueRule<TContext> defaultValuePolicy) =>
        defaultValuePolicy(ref reader, writer, context, ref propPath);

    /// <summary>
    /// Masks a value of any JSON type with the call's <see cref="ValueMaskStrategy"/> (or <paramref name="strategy"/>);
    /// a container is masked whole and skipped; a <c>null</c> value stays <c>null</c>.
    /// </summary>
    /// <param name="tag">Tag handed to the strategy.</param>
    /// <param name="strategy">Strategy of this rule; the call's when <c>null</c>.</param>
    public static ObserveRule<TContext> ApplyTagPolicy(MaskTag tag, ValueMaskStrategy? strategy = null)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext _,
            int __,
            ref JsonWalk propPath,
            ValueRule<TContext> ___) =>
        {
            if (reader.TokenType is Null)
            {
                writer.WriteNullValue();
                return;
            }

            TagMasking.Mask(ref reader, writer, tag, strategy ?? propPath.Options.Strategy ?? ValueMaskStrategy.Default, ref propPath);
        };
    }

    /// <summary>
    /// Masks a value of any JSON type with a function: a string arrives decoded, a number or boolean as its literal, an
    /// object or array is skipped unread and arrives as <c>null</c>; with <see cref="MaskNulls.Keep"/> a <c>null</c>
    /// value stays <c>null</c> without calling it.
    /// </summary>
    public static ObserveRule<TContext> ApplyFunctionPolicy(Func<string?, TContext, string?> maskingRule, string? constant, MaskNulls nulls) =>
        ApplyMaskPolicy(
            constant is not null
                ? (ref Utf8JsonReader _, TContext _) => constant
                : (ref Utf8JsonReader reader, TContext context) => maskingRule(ScalarText(ref reader), context),
            keepNull: nulls == MaskNulls.Keep);

    /// <summary>
    /// A custom rule: hands the value to <paramref name="rule"/> with the enclosing default policy for
    /// <see cref="JsonValueContext{TContext}.WriteDefault"/>.
    /// </summary>
    public static ObserveRule<TContext> ApplyCustomRule(JsonValueRule<TContext> rule) =>
        (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref JsonWalk walk, ValueRule<TContext> defaultValue) =>
        {
            var value = new JsonValueContext<TContext>(ref reader, writer, context, depth, ref walk, defaultValue, null);
            rule(ref value);
            value.CopyBack(ref reader, ref walk);
        };

    /// <summary>
    /// Masks a value of any JSON type; the function receives <c>true</c> or <c>false</c>, and <c>null</c> for anything else.
    /// </summary>
    public static ObserveRule<TContext> ApplyBoolPolicy(Func<bool?, TContext, string?> maskingRule) =>
        ApplyMaskPolicy(
            (ref Utf8JsonReader reader, TContext context) => maskingRule(reader.TokenType switch
            {
                True => true,
                False => false,
                _ => null,
            }, context),
            keepNull: false);

    /// <summary>
    /// Masks a value of any JSON type; the function receives a number that fits <see cref="int"/>, and <c>null</c> for anything else.
    /// </summary>
    public static ObserveRule<TContext> ApplyIntPolicy(Func<int?, TContext, string?> maskingRule) =>
        ApplyMaskPolicy(
            (ref Utf8JsonReader reader, TContext context) =>
                maskingRule(reader.TokenType is Number && reader.TryGetInt32(out var value) ? value : null, context),
            keepNull: false);

    /// <summary>
    /// Masks a value of any JSON type; the function receives a number that fits <see cref="long"/>, and <c>null</c> for anything else.
    /// </summary>
    public static ObserveRule<TContext> ApplyLongPolicy(Func<long?, TContext, string?> maskingRule) =>
        ApplyMaskPolicy(
            (ref Utf8JsonReader reader, TContext context) =>
                maskingRule(reader.TokenType is Number && reader.TryGetInt64(out var value) ? value : null, context),
            keepNull: false);

    /// <summary>
    /// Masks a value of any JSON type; the function receives a number that fits <see cref="decimal"/>, and <c>null</c> for anything else.
    /// </summary>
    public static ObserveRule<TContext> ApplyDecimalPolicy(Func<decimal?, TContext, string?> maskingRule) =>
        ApplyMaskPolicy(
            (ref Utf8JsonReader reader, TContext context) =>
                maskingRule(reader.TokenType is Number && reader.TryGetDecimal(out var value) ? value : null, context),
            keepNull: false);

    private delegate string? MaskToken(ref Utf8JsonReader reader, TContext context);

    /// <summary>
    /// Writes the function's replacement for the current value whatever its type, then moves past it; a container is never read.
    /// </summary>
    private static ObserveRule<TContext> ApplyMaskPolicy(MaskToken mask, bool keepNull)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int _,
            ref JsonWalk propPath,
            ValueRule<TContext> __) =>
        {
            if (keepNull && reader.TokenType is Null)
            {
                writer.WriteNullValue();
                return;
            }

            var result = mask(ref reader, context);
            if (result is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.MaskOutput = true;
                writer.WriteStringValue(result);
                writer.MaskOutput = false;
            }

            if (reader.TokenType is StartObject or StartArray && !reader.TrySkip())
            {
                propPath.Stop();
            }
        };
    }

    /// <summary>
    /// The whole text of a string, number or boolean token; <c>null</c> for anything else.
    /// </summary>
    private static string? ScalarText(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is not (JsonTokenType.String or Number or True or False))
        {
            return null;
        }

        if (reader.TokenType is JsonTokenType.String && (reader.ValueIsEscaped || reader.HasValueSequence))
        {
            var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                return Encoding.UTF8.GetString(buffer.AsSpan(0, reader.CopyString(buffer)));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        return reader.HasValueSequence ? Encoding.UTF8.GetString(reader.ValueSequence) : Encoding.UTF8.GetString(reader.ValueSpan);
    }

    /// <summary>
    /// Apply masking policy for values.
    /// </summary>
    /// <param name="policies">Property masking policies.</param>
    /// <param name="valuePolicy">Value masking delegate.</param>
    public static ObserveRule<TContext> ApplyValuePolicy(JsonObserverItem<TContext>[] policies, ValueRule<TContext>? valuePolicy)
    {
        var defaultPolicy = GetApplyDefaultPolicy(valuePolicy, UnknownContainers.Create(policies));
        var lastReader = LastReader(policies);

        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref JsonWalk propPath,
            ValueRule<TContext> defaultValuePolicy) =>
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                case Number:
                case True:
                case False:
                case Null:
                    var tokenType = reader.TokenType;
                    if (lastReader >= 0)
                    {
                        RunReads(policies, lastReader, depth, ref propPath, ref reader, context);
                    }

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

    internal static ObserveRule<TContext> ApplyObjPolicy(JsonObserverItem<TContext>[] policies, ValueRule<TContext>? valuePolicy)
        => ApplyObjPolicy(policies, valuePolicy, UnknownContainers.Create(policies));

    internal static ObserveRule<TContext> ApplyArrayPolicy(JsonObserverItem<TContext>[] policies, ValueRule<TContext>? valuePolicy)
        => ApplyArrayPolicy(policies, valuePolicy, UnknownContainers.Create(policies));

    private static ObserveRule<TContext> ApplyObjPolicy(
        JsonObserverItem<TContext>[] policies,
        ValueRule<TContext>? valuePolicy,
        UnknownContainers unknown)
    {
        var defaultPolicy = GetApplyDefaultPolicy(valuePolicy, unknown);
        var lastReader = LastReader(policies);

        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref JsonWalk propPath,
            ValueRule<TContext> defaultValuePolicy) =>
        {
            var effective = valuePolicy ?? defaultValuePolicy;

            if (reader.TokenType != StartObject)
            {
                throw new JsonObserverException("Wrong path");
            }

            RuntimeHelpers.EnsureSufficientExecutionStack();
            writer.WriteStartObject();
            var comments = propPath.Comments is not null;
            var previousOpen = false;
            JsonObserverItem<TContext>? previousItem = null;
            var previousMasked = false;

            while (true)
            {
                if (propPath.Stopped || writer.Stopped || !reader.Read())
                {
                    if (previousOpen)
                    {
                        propPath.RemovePropertyName();
                    }

                    propPath.Stop();
                    return;
                }

                switch (reader.TokenType)
                {
                    case Comment:
                        JsonComments.OnComment(ref reader, writer, ref propPath, previousOpen, previousItem, previousMasked);
                        break;
                    case PropertyName:
                        if (previousOpen)
                        {
                            propPath.RemovePropertyName();
                            previousOpen = false;
                        }

                        propPath.AddPropertyName(ref reader);

                        if (!ReadMemberValue(ref reader, ref propPath))
                        {
                            propPath.RemovePropertyName();
                            propPath.Stop();
                            return;
                        }

                        var tokenType = reader.TokenType;
                        if (lastReader >= 0)
                        {
                            RunReads(policies, lastReader, depth, ref propPath, ref reader, context);
                        }

                        var (matchPolicy, nextDepth) = MatchPolicy(policies, depth, ref propPath, tokenType);
                        if (comments)
                        {
                            (previousMasked, previousItem) = CommentOwner(matchPolicy, effective, tokenType, ref propPath);
                            JsonComments.Flush(writer, ref propPath, CommentKind.Before, previousItem?.CommentRuleFor(CommentKind.Before), previousMasked);
                        }

                        writer.WritePropertyName(propPath.CurrentUtf8);
                        if (matchPolicy is not null)
                        {
                            matchPolicy.Apply(ref reader, writer, context, nextDepth, ref propPath, effective);
                        }
                        else
                        {
                            defaultPolicy(ref reader, writer, context, nextDepth, ref propPath, effective);
                        }

                        if (comments)
                        {
                            propPath.LastValueEnd = reader.BytesConsumed;
                            previousOpen = true;
                        }
                        else
                        {
                            propPath.RemovePropertyName();
                        }

                        break;
                    case EndObject:
                        if (previousOpen)
                        {
                            propPath.RemovePropertyName();
                        }

                        if (comments)
                        {
                            JsonComments.Flush(writer, ref propPath, CommentKind.After, null, ownerMasked: false);
                        }

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

    private static ObserveRule<TContext> ApplyArrayPolicy(
        JsonObserverItem<TContext>[] policies,
        ValueRule<TContext>? valuePolicy,
        UnknownContainers unknown)
    {
        var defaultPolicy = GetApplyDefaultPolicy(valuePolicy, unknown);
        var lastReader = LastReader(policies);

        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref JsonWalk propPath,
            ValueRule<TContext> defaultValuePolicy) =>
        {
            var effective = valuePolicy ?? defaultValuePolicy;
            if (reader.TokenType != StartArray)
            {
                throw new JsonObserverException("Wrong path");
            }

            RuntimeHelpers.EnsureSufficientExecutionStack();
            writer.WriteStartArray();
            var comments = propPath.Comments is not null;
            var previousOpen = false;
            JsonObserverItem<TContext>? previousItem = null;
            var previousMasked = false;

            var index = 0;
            while (true)
            {
                if (propPath.Stopped || writer.Stopped || !reader.Read())
                {
                    if (previousOpen)
                    {
                        propPath.RemovePropertyName();
                    }

                    propPath.Stop();
                    return;
                }

                switch (reader.TokenType)
                {
                    case Comment:
                        JsonComments.OnComment(ref reader, writer, ref propPath, previousOpen, previousItem, previousMasked);
                        break;
                    case StartObject:
                    case StartArray:
                    case JsonTokenType.String:
                    case Number:
                    case True:
                    case False:
                    case Null:
                        if (previousOpen)
                        {
                            propPath.RemovePropertyName();
                            previousOpen = false;
                        }

                        var tokenType = reader.TokenType;

                        propPath.AddArrayItem(index++);
                        if (lastReader >= 0)
                        {
                            RunReads(policies, lastReader, depth, ref propPath, ref reader, context);
                        }

                        var (matchPolicy, nextDepth) = MatchPolicy(policies, depth, ref propPath, tokenType);
                        if (comments)
                        {
                            (previousMasked, previousItem) = CommentOwner(matchPolicy, effective, tokenType, ref propPath);
                            JsonComments.Flush(writer, ref propPath, CommentKind.Before, previousItem?.CommentRuleFor(CommentKind.Before), previousMasked);
                        }

                        if (matchPolicy is not null)
                        {
                            matchPolicy.Apply(ref reader, writer, context, nextDepth, ref propPath, effective);
                        }
                        else
                        {
                            defaultPolicy(ref reader, writer, context, nextDepth, ref propPath, effective);
                        }

                        if (comments)
                        {
                            propPath.LastValueEnd = reader.BytesConsumed;
                            previousOpen = true;
                        }
                        else
                        {
                            propPath.RemovePropertyName();
                        }

                        break;
                    case EndArray:
                        if (previousOpen)
                        {
                            propPath.RemovePropertyName();
                        }

                        if (comments)
                        {
                            JsonComments.Flush(writer, ref propPath, CommentKind.After, null, ownerMasked: false);
                        }

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

    /// <summary>
    /// Moves from a property name to its value, keeping the comments in between for the member.
    /// </summary>
    private static bool ReadMemberValue(ref Utf8JsonReader reader, ref JsonWalk walk)
    {
        while (reader.Read())
        {
            if (reader.TokenType != Comment)
            {
                return true;
            }

            walk.Pending!.Add(ref reader, walk.StyleAt(reader.TokenStartIndex));
        }

        return false;
    }

    /// <summary>
    /// Whether the member's value is masked, and the rule whose comment rules apply to its comments.
    /// </summary>
    internal static (bool Masked, JsonObserverItem<TContext>? RuleItem) CommentOwner(
        JsonObserverItem<TContext>? item,
        ValueRule<TContext> effective,
        JsonTokenType tokenType,
        ref JsonWalk walk)
    {
        if (item is not null)
        {
            var masked = item.Info.Outcome switch
            {
                PathOutcome.Masked => !(tokenType is Null && item.Info.KeepsNull),
                PathOutcome.Custom => true,
                _ => false,
            };
            return (masked, item);
        }

        if (effective.Target is RelativeValuePolicy<TContext> relative)
        {
            var (relativeItem, _) = MatchPolicy(relative.Items, 0, ref walk, tokenType);
            return relativeItem is not null
                ? CommentOwner(relativeItem, relative.Fallback.Rule, tokenType, ref walk)
                : CommentOwner(null, relative.Fallback.Rule, tokenType, ref walk);
        }

        if (tokenType is StartObject or StartArray or Null)
        {
            return (false, null);
        }

        return (!ReferenceEquals(effective, BuiltInPolicies<TContext>.BlockList), null);
    }

    private static ObserveRule<TContext> GetApplyDefaultPolicy(ValueRule<TContext>? valuePolicy, UnknownContainers unknown)
    {
        return (
            ref Utf8JsonReader reader,
            JsonWriter writer,
            TContext context,
            int depth,
            ref JsonWalk propPath,
            ValueRule<TContext> defaultValuePolicy) =>
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
        ref JsonWalk path,
        JsonTokenType tokenType)
    {
        foreach (var policyItem in policies)
        {
            if (policyItem.Reader is not null)
            {
                continue;
            }

            var (success, nextDepth) = policyItem.Match(depth, ref path, tokenType);
            if (!success)
            {
                continue;
            }

            return (policyItem, nextDepth + depth);
        }

        return (null, depth);
    }

    /// <summary>
    /// Index of the last read rule, or <c>-1</c> when there is none, so that a pass without read rules skips <see cref="RunReads"/>.
    /// </summary>
    internal static int LastReader(JsonObserverItem<TContext>[] policies) =>
        global::System.Array.FindLastIndex(policies, static p => p.Reader is not null);

    /// <summary>
    /// Runs every read rule up to <paramref name="lastReader"/> that matches the current value, wherever it stands
    /// relative to the rule that writes the value.
    /// </summary>
    internal static void RunReads(
        JsonObserverItem<TContext>[] policies,
        int lastReader,
        int depth,
        ref JsonWalk path,
        ref Utf8JsonReader reader,
        TContext context)
    {
        for (var i = 0; i <= lastReader; i++)
        {
            var policyItem = policies[i];
            if (policyItem.Reader is not null && policyItem.Match(depth, ref path, reader.TokenType).success)
            {
                policyItem.Reader(ref reader, context);
            }
        }
    }

    internal (bool success, int depth) Match(int depth, ref JsonWalk propPath, JsonTokenType token) => propMatch(depth, ref propPath, token);

    internal void Apply(
        ref Utf8JsonReader reader,
        JsonWriter writer,
        TContext context,
        int depth,
        ref JsonWalk propPath,
        ValueRule<TContext> defaultValue) =>
        masking(ref reader, writer, context, depth, ref propPath, defaultValue);

    /// <summary>
    /// Policies for containers no rule matched, built once so that no delegate mutates captured state at call time.
    /// </summary>
    private sealed class UnknownContainers
    {
        public ObserveRule<TContext> Obj { get; private set; } = null!;
        public ObserveRule<TContext> Array { get; private set; } = null!;

        public static UnknownContainers Create(JsonObserverItem<TContext>[] policies)
        {
            var unknown = new UnknownContainers();
            unknown.Obj = ApplyObjPolicy(policies, null, unknown);
            unknown.Array = ApplyArrayPolicy(policies, null, unknown);
            return unknown;
        }
    }
}
