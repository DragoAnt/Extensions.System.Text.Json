using Extensions.System.Text.Json.Observer.Strategies;

namespace Extensions.System.Text.Json.Observer.Builders;

/// <summary>
/// Policy builder for JSON values
/// </summary>
/// <remarks>
/// This builder is for values inside any parent.
/// </remarks>
public readonly struct JsonValuePolicyBuilder<TContext>(bool relative, JsonObserverValueDelegate<TContext>? builderDefaultValuePolicy)
{
    private readonly bool _relative = relative;
    private readonly List<JsonObserveringItem<TContext>> _policies = [];

    /// <summary>
    /// Matches property names by specific condition.
    /// </summary>
    /// <param name="match">Array of property matching.</param>
    public PropertyMaskingStrategyBuilder Match(params PropMatchingStrategy[] match) =>
        new(this, new PropertyPathMatch(match), builderDefaultValuePolicy);

    internal static JsonObserverDelegate<TContext> Build(JsonValuePolicyBuilder<TContext> builder) => builder.Build();

    private JsonValuePolicyBuilder<TContext> AddValueProp(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverDelegate<TContext> policy)
    {
        var item = new JsonObserveringItem<TContext>((int depth, ref PropertyPath path, JsonTokenType type) =>
        {
            var (success, nextDepth) = propNameMatch(depth, ref path);

            if (!success || !type.IsValueToken())
            {
                return (false, 0);
            }

            return (true, nextDepth);
        }, policy);
        _policies.Add(item);
        return this;
    }

    private JsonObserverDelegate<TContext> Build() => JsonObserveringItem<TContext>.ApplyValuePolicy([.. _policies], builderDefaultValuePolicy);

    /// <summary>
    /// Masking strategy builder for string properties.
    /// </summary>
    /// <param name="builder">Instance of <see cref="JsonValuePolicyBuilder{TContext}"/>.</param>
    /// <param name="propNameMatch">Property path matching.</param>
    /// <param name="builderDefaultValuePolicy">Default value policy.</param>
    public readonly ref struct PropertyMaskingStrategyBuilder(
        JsonValuePolicyBuilder<TContext> builder,
        PropertyPathMatch propNameMatch,
        JsonObserverValueDelegate<TContext>? builderDefaultValuePolicy)
    {
        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.String"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> MaskStr(Func<string?, TContext, string?> strategy)
            => MaskStr((StringMaskingStrategy<TContext>)strategy);

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.String"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> MaskStr(StringMaskingStrategy<TContext> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueStringPolicy(strategy, builderDefaultValuePolicy));


        /// <summary>
        /// Reads value <see cref="JsonTokenType.String"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> ReadStr(Action<string?, TContext> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueStringPolicy((v, c) =>
            {
                strategy(v, c);
                return v;
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueIntPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> ReadInt(Action<int?, TContext> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueIntPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueLongPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> ReadLong(Action<long?, TContext> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueLongPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueDecimalPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> ReadDecimal(Action<decimal?, TContext> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueDecimalPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.True"/>  or <see cref="JsonTokenType.False"/>.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueBoolPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads value <see cref="JsonTokenType.True"/> or <see cref="JsonTokenType.False"/>.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> ReadBool(Action<bool?, TContext> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueBoolPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for any value.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> MaskRawValue(Func<string?, TContext, string?> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueRawPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads any value as string.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonValuePolicyBuilder<TContext> ReadRaw(Action<string?, TContext> strategy)
            => MaskValue(JsonObserveringItem<TContext>.ApplyValueRawPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));


        /// <summary>
        /// Leave property value untouched
        /// </summary>
        public JsonValuePolicyBuilder<TContext> Unmasked() =>
            MaskValue((
                    ref Utf8JsonReader reader,
                    JsonWriter writer,
                    TContext context,
                    int depth,
                    ref PropertyPath propPath,
                    JsonObserverValueDelegate<TContext> _) =>
                JsonObserverValuePolicies<TContext>.BlockList(ref reader, writer, context, ref propPath));

        public JsonValuePolicyBuilder<TContext> MaskValue(JsonObserverDelegate<TContext> policy)
            => builder.AddValueProp(builder._relative ? propNameMatch.RelativeMatch : propNameMatch.AbsoluteMatch, policy);
    }
}