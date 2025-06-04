using Extensions.System.Text.Json.Observer.Strategies;
using static System.Text.Json.JsonTokenType;

namespace Extensions.System.Text.Json.Observer.Builders;

/// <summary>
/// Policy builder for JSON objects.
/// </summary>
/// <remarks>
/// This builder is for values inside specified parent with defined path.
/// </remarks>
public readonly struct JsonObjBuilder<TContext>(JsonObserverValueDelegate<TContext>? builderDefaultValuePolicy)
{
    private readonly List<JsonObserverItem<TContext>> _policies = [];

    /// <summary>
    /// Matches property names by specific condition.
    /// </summary>
    /// <param name="match">Array of property matching.</param>
    public PropertyMaskingStrategyBuilder Match(PropMatchingStrategy match) =>
        new(this, new PropertyPathMatch(new[] { match }), builderDefaultValuePolicy);

    /// <summary>
    /// Matches property names by specific condition.
    /// </summary>
    /// <param name="match">Array of property matching.</param>
    public PropertyMaskingStrategyBuilder Match(params PropMatchingStrategy[] match) =>
        new(this, new PropertyPathMatch(match), builderDefaultValuePolicy);

    internal static JsonObserverDelegate<TContext> Build(JsonObjBuilder<TContext> builder) => builder.Build();
    private JsonObserverDelegate<TContext> Build() => JsonObserverItem<TContext>.ApplyObjPolicy([.. _policies], builderDefaultValuePolicy);

    private JsonObjBuilder<TContext> AddValue(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverDelegate<TContext> policy) =>
        Add((int depth, ref PropertyPath path, JsonTokenType type) =>
        {
            var (success, propDepth) = propNameMatch(depth, ref path);

            if (!success || !type.IsValueToken())
            {
                return (false, 0);
            }

            return (true, propDepth);
        }, policy);

    private JsonObjBuilder<TContext> Add(JsonPropertyMatchDelegate propMatch, JsonObserverDelegate<TContext> policy)
    {
        var item = new JsonObserverItem<TContext>(propMatch, policy);
        _policies.Add(item);
        return this;
    }

    /// <summary>
    /// Masking strategy builder for string properties.
    /// </summary>
    /// <param name="builder">Instance of <see cref="JsonObjBuilder{TContext}"/>.</param>
    /// <param name="propNameMatch">Property path matching.</param>
    /// <param name="builderDefaultValuePolicy">Default value policy.</param>
    public readonly ref struct PropertyMaskingStrategyBuilder(
        JsonObjBuilder<TContext> builder,
        PropertyPathMatch propNameMatch,
        JsonObserverValueDelegate<TContext>? builderDefaultValuePolicy)
    {
        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.String"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> MaskStr(Func<string?, TContext, string?> strategy)
            => MaskStr((StringMaskingStrategy<TContext>)strategy);

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.String"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> MaskStr(StringMaskingStrategy<TContext> strategy) =>
            MaskValue(JsonObserverItem<TContext>.ApplyValueStringPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads value <see cref="JsonTokenType.String"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> ReadStr(Action<string?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueStringPolicy((v, c) =>
            {
                strategy(v, c);
                return v;
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueIntPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> ReadInt(Action<int?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueIntPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueLongPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> ReadLong(Action<long?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueLongPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueDecimalPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> ReadDecimal(Action<decimal?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueDecimalPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.True"/>  or <see cref="JsonTokenType.False"/>.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueBoolPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads value <see cref="JsonTokenType.True"/> or <see cref="JsonTokenType.False"/>.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> ReadBool(Action<bool?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueBoolPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for any value.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> MaskRawValue(Func<string?, TContext, string?> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueRawPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads any value as string.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonObjBuilder<TContext> ReadRaw(Action<string?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueRawPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Masking rule for value property.
        /// </summary>
        /// <param name="policy">Masking policy.</param>
        public JsonObjBuilder<TContext> MaskValue(JsonObserverValueDelegate<TContext> policy) =>
            MaskValue(
                (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref PropertyPath propPath, JsonObserverValueDelegate<TContext> _) =>
                    policy(ref reader, writer, context, ref propPath));

        /// <summary>
        /// Masking rule for value property.
        /// </summary>
        /// <param name="policy">Masking policy.</param>
        public JsonObjBuilder<TContext> MaskValue(JsonObserverDelegate<TContext> policy)
            => builder.AddValue(propNameMatch.AbsoluteMatch, policy);

        /// <summary>
        /// Leave property value untouched
        /// </summary>
        public JsonObjBuilder<TContext> Unmasked() =>
            builder.AddValue(
                propNameMatch.AbsoluteMatch,
                (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref PropertyPath propPath, JsonObserverValueDelegate<TContext> _) =>
                    JsonObserverValuePolicies<TContext>.BlockList(ref reader, writer, context, ref propPath));

        /// <summary>
        /// Masking rule for JSON object property.
        /// </summary>
        /// <param name="init">Masking condition builder.</param>
        /// <param name="defaultValuePolicy">Default masking policy.</param>
        public JsonObjBuilder<TContext> Obj(
            Action<JsonObjBuilder<TContext>> init,
            JsonObserverValueDelegate<TContext>? defaultValuePolicy = null) =>
            Obj(JsonObserverItem<TContext>.Obj(init, defaultValuePolicy ?? builderDefaultValuePolicy));

        /// <summary>
        /// Masking rule for JSON object property.
        /// </summary>
        /// <param name="policy">Masking policy.</param>
        public JsonObjBuilder<TContext> Obj(JsonObserverDelegate<TContext> policy)
        {
            var match = propNameMatch.AbsoluteMatch;
            return builder.Add((int depth, ref PropertyPath path, JsonTokenType type) =>
            {
                var (success, nextDepth) = match(depth, ref path);

                if (!success || type != StartObject)
                {
                    return (false, 0);
                }
                return (true, nextDepth);
            }, policy);
        }

        /// <summary>
        /// Masking rule for JSON array property.
        /// </summary>
        /// <param name="init">Masking condition builder.</param>
        /// <param name="defaultValuePolicy">Default masking policy.</param>
        public JsonObjBuilder<TContext> Array(
            Action<JsonArrayBuilder<TContext>> init,
            JsonObserverValueDelegate<TContext>? defaultValuePolicy = null) =>
            Array(JsonObserverItem<TContext>.Array(init, defaultValuePolicy ?? builderDefaultValuePolicy));

        /// <summary>
        /// Masking rule for JSON object property.
        /// </summary>
        /// <param name="policy">Masking policy.</param>
        public JsonObjBuilder<TContext> Array(JsonObserverDelegate<TContext> policy)
        {
            var match = propNameMatch.AbsoluteMatch;
            return builder.Add((int depth, ref PropertyPath path, JsonTokenType type) =>
            {
                var (success, nextDepth) = match(depth, ref path);

                if (!success || type != StartArray)
                {
                    return (false, 0);
                }
                return (true, nextDepth);
            }, policy);
        }
    }
}