using DragoAnt.System.Text.Json.Observer.Builders;
using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// What happens to values no rule matches: one of the built-in <see cref="ValuePolicy"/> values (which convert to it),
/// a set of <see cref="JsonValuePolicy.AnyDepth(Action{JsonAnyDepthBuilder{NoContext}}, JsonValuePolicy{NoContext}?)"/>
/// rules, or a custom rule.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
public sealed class JsonValuePolicy<TContext>
{
    private JsonValuePolicy(ValueRule<TContext> rule, string name, ValuePolicy? builtIn, RelativeValuePolicy<TContext>? relative)
    {
        Rule = rule;
        Name = name;
        BuiltIn = builtIn;
        Relative = relative;
    }

    internal static JsonValuePolicy<TContext> Default { get; } = From(ValuePolicy.AllowList);

    internal ValueRule<TContext> Rule { get; }

    internal string Name { get; }

    internal ValuePolicy? BuiltIn { get; }

    internal RelativeValuePolicy<TContext>? Relative { get; }

    /// <summary>
    /// A custom policy: <paramref name="rule"/> is called for every string, number, boolean or <c>null</c> value no
    /// rule matches and writes exactly one value. Objects and arrays are descended with the same policy.
    /// </summary>
    /// <param name="rule">The policy.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <c>null</c>.</exception>
    public static JsonValuePolicy<TContext> Custom(JsonValueRule<TContext> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new JsonValuePolicy<TContext>(
            (ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref JsonWalk walk) =>
            {
                var value = new JsonValueContext<TContext>(ref reader, writer, context, 0, ref walk, BuiltInPolicies<TContext>.AllowList, null);
                rule(ref value);
                value.CopyBack(ref reader, ref walk);
            },
            "custom default policy",
            null,
            null);
    }

    internal static JsonValuePolicy<TContext> AnyDepth(RelativeValuePolicy<TContext> relative) =>
        new(relative.Invoke, "AnyDepth", null, relative);

    /// <summary>
    /// The built-in policy <paramref name="policy"/>.
    /// </summary>
    /// <param name="policy">A built-in policy such as <see cref="ValuePolicy.BlockList"/>.</param>
    public static implicit operator JsonValuePolicy<TContext>(ValuePolicy policy) => From(policy);

    /// <summary>
    /// The policy's name, for example <c>AllowList</c>, <c>AnyDepth</c> or <c>custom default policy</c>.
    /// </summary>
    public override string ToString() => Name;

    private static JsonValuePolicy<TContext> From(ValuePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Kind switch
        {
            ValuePolicyKind.BlockList => Cache.BlockList,
            ValuePolicyKind.AllowList => Cache.AllowList,
            ValuePolicyKind.NullList => Cache.NullList,
            _ => new JsonValuePolicy<TContext>(BuiltInPolicies<TContext>.Tagged(policy.Tag), policy.ToString(), policy, null),
        };
    }

    private static class Cache
    {
        public static readonly JsonValuePolicy<TContext> BlockList =
            new(BuiltInPolicies<TContext>.BlockList, nameof(ValuePolicy.BlockList), ValuePolicy.BlockList, null);

        public static readonly JsonValuePolicy<TContext> AllowList =
            new(BuiltInPolicies<TContext>.AllowList, nameof(ValuePolicy.AllowList), ValuePolicy.AllowList, null);

        public static readonly JsonValuePolicy<TContext> NullList =
            new(BuiltInPolicies<TContext>.NullList, nameof(ValuePolicy.NullList), ValuePolicy.NullList, null);
    }
}

/// <summary>
/// Policies with rules of their own, and custom policies.
/// </summary>
public static class JsonValuePolicy
{
    /// <summary>
    /// A default policy with its own rules that match the end of a property's path at any depth, for example every
    /// <c>password</c> or every <c>card.number</c>, wherever it is nested.
    /// </summary>
    /// <param name="init">Adds the rules, see <see cref="JsonAnyDepthBuilder{TContext}"/>.</param>
    /// <param name="fallback">Policy for values none of these rules match; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    /// <returns>A policy to pass where a default policy is expected.</returns>
    public static JsonValuePolicy<NoContext> AnyDepth(
        Action<JsonAnyDepthBuilder<NoContext>> init,
        JsonValuePolicy<NoContext>? fallback = null)
        => AnyDepth<NoContext>(init, fallback);

    /// <summary>
    /// A default policy with its own rules, for an observer with a context, that match the end of a property's path at
    /// any depth.
    /// </summary>
    /// <param name="init">Adds the rules, see <see cref="JsonAnyDepthBuilder{TContext}"/>.</param>
    /// <param name="fallback">Policy for values none of these rules match; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    /// <returns>A policy to pass where a default policy is expected.</returns>
    public static JsonValuePolicy<TContext> AnyDepth<TContext>(
        Action<JsonAnyDepthBuilder<TContext>> init,
        JsonValuePolicy<TContext>? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(init);
        var effectiveFallback = fallback ?? JsonValuePolicy<TContext>.Default;
        var builder = new JsonAnyDepthBuilder<TContext>(effectiveFallback);
        init(builder);
        var relative = new RelativeValuePolicy<TContext>(
            JsonAnyDepthBuilder<TContext>.Build(builder),
            JsonAnyDepthBuilder<TContext>.BuildItems(builder),
            effectiveFallback);

        return JsonValuePolicy<TContext>.AnyDepth(relative);
    }

    /// <summary>
    /// A custom policy for an observer without a context.
    /// </summary>
    /// <param name="rule">Called for every string, number, boolean or <c>null</c> value no rule matches; writes exactly one value.</param>
    public static JsonValuePolicy<NoContext> Custom(JsonValueRule<NoContext> rule) => JsonValuePolicy<NoContext>.Custom(rule);
}

/// <summary>
/// The engine side of the built-in policies.
/// </summary>
internal static class BuiltInPolicies<TContext>
{
    public static readonly ValueRule<TContext> BlockList = WriteUnchanged;
    public static readonly ValueRule<TContext> AllowList = WriteStars;
    public static readonly ValueRule<TContext> NullList = WriteNull;

    public static ValueRule<TContext> Tagged(MaskTag tag) =>
        (ref Utf8JsonReader reader, JsonWriter writer, TContext _, ref JsonWalk walk) =>
        {
            if (reader.TokenType is Null)
            {
                writer.WriteNullValue();
                return;
            }

            TagMasking.Mask(ref reader, writer, tag, ref walk);
        };

    /// <summary>
    /// Writes <c>"***"</c> for an object or array and moves past it unread.
    /// </summary>
    public static void MaskContainer(ref Utf8JsonReader reader, JsonWriter writer, ref JsonWalk walk)
    {
        writer.MaskOutput = true;
        writer.WriteStringValue("***"u8);
        writer.MaskOutput = false;
        if (!reader.TrySkip())
        {
            walk.Stop();
        }
    }

    private static void WriteNull(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref JsonWalk walk)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            case Number:
            case True:
            case False:
            case Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonObserverException("Wrong path");
        }
    }

    private static void WriteUnchanged(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref JsonWalk walk)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                writer.CopyStringValue(ref reader);
                break;
            case Number:
                writer.CopyRawValue(ref reader);
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
            default:
                throw new JsonObserverException("Wrong path");
        }
    }

    private static void WriteStars(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref JsonWalk walk)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            case Number:
            case True:
            case False:
                writer.MaskOutput = true;
                writer.WriteStringValue("***"u8);
                writer.MaskOutput = false;
                break;
            case Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonObserverException("Wrong path");
        }
    }
}
