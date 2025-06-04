using Extensions.System.Text.Json.Observer.Strategies;
using static System.Text.Json.JsonTokenType;

namespace Extensions.System.Text.Json.Observer.Builders;

/// <summary>
/// Policy builder for JSON arrays.
/// </summary>
/// <remarks>
/// This builder is for values inside specified parent with defined path.
/// </remarks>
public readonly struct JsonArrayBuilder<TContext>(JsonObserverValueDelegate<TContext>? builderDefaultValuePolicy)
{
    private readonly List<JsonObserverItem<TContext>> _policies = [];

    /// <summary>
    /// Masking initialization for specified object inside array.
    /// </summary>
    /// <param name="init">Masking condition builder.</param>
    /// <param name="defaultValuePolicy">Default masking policy.</param>
    public JsonArrayBuilder<TContext> Obj(
        Action<JsonObjBuilder<TContext>> init,
        JsonObserverValueDelegate<TContext>? defaultValuePolicy = null) =>
        Obj(JsonObserverItem<TContext>.Obj(init, defaultValuePolicy ?? builderDefaultValuePolicy));

    /// <summary>
    /// Masking initialization for the array.
    /// </summary>
    /// <param name="init">Masking condition builder.</param>
    /// <param name="defaultValuePolicy">Default masking policy.</param>
    public JsonArrayBuilder<TContext> Array(
        Action<JsonArrayBuilder<TContext>> init,
        JsonObserverValueDelegate<TContext>? defaultValuePolicy = null) =>
        Array(JsonObserverItem<TContext>.Array(init, defaultValuePolicy ?? builderDefaultValuePolicy));

    /// <summary>
    /// Add masking for value <see cref="JsonTokenType.String"/>
    /// </summary>
    /// <param name="strategy">Masking strategy</param>
    public JsonArrayBuilder<TContext> MaskStr(Func<string?, TContext, string?> strategy)
        => MaskStr((StringMaskingStrategy<TContext>)strategy);

    /// <summary>
    /// Add masking for value <see cref="JsonTokenType.String"/>
    /// </summary>
    /// <param name="strategy">Masking strategy</param>
    public JsonArrayBuilder<TContext> MaskStr(StringMaskingStrategy<TContext> strategy) =>
        MaskValue(JsonObserverItem<TContext>.ApplyValueStringPolicy(strategy, builderDefaultValuePolicy));

    /// <summary>
        /// Reads value <see cref="JsonTokenType.String"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> ReadStr(Action<string?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueStringPolicy((v, c) =>
            {
                strategy(v, c);
                return v;
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueIntPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> ReadInt(Action<int?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueIntPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueLongPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> ReadLong(Action<long?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueLongPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueDecimalPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads value <see cref="JsonTokenType.Number"/>
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> ReadDecimal(Action<decimal?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueDecimalPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for value <see cref="JsonTokenType.True"/>  or <see cref="JsonTokenType.False"/>.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueBoolPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads value <see cref="JsonTokenType.True"/> or <see cref="JsonTokenType.False"/>.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> ReadBool(Action<bool?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueBoolPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

        /// <summary>
        /// Add masking for any value.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> MaskRawValue(Func<string?, TContext, string?> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueRawPolicy(strategy, builderDefaultValuePolicy));

        /// <summary>
        /// Reads any value as string.
        /// </summary>
        /// <param name="strategy">Masking strategy</param>
        public JsonArrayBuilder<TContext> ReadRaw(Action<string?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ApplyValueRawPolicy((v, c) =>
            {
                strategy(v, c);
                return v?.ToString();
            }, builderDefaultValuePolicy));

    /// <summary>
    /// Masking rule for value property.
    /// </summary>
    /// <param name="policy">Masking policy.</param>
    public JsonArrayBuilder<TContext> MaskValue(JsonObserverValueDelegate<TContext> policy) =>
        MaskValue(
            (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref PropertyPath propPath, JsonObserverValueDelegate<TContext> _) =>
                policy(ref reader, writer, context, ref propPath));

    /// <summary>
    /// Masking rule for value property.
    /// </summary>
    /// <param name="policy">Masking policy.</param>
    public JsonArrayBuilder<TContext> MaskValue(JsonObserverDelegate<TContext> policy)
        => Add(type => type.IsValueToken(), policy);

    /// <summary>
    /// Make the array always visible.
    /// </summary>
    public JsonArrayBuilder<TContext> Unmasked() => MaskValue(JsonObserverValuePolicies<TContext>.BlockList);

    internal static JsonObserverDelegate<TContext> Build(JsonArrayBuilder<TContext> builder) => builder.Build();

    private JsonArrayBuilder<TContext> Array(JsonObserverDelegate<TContext> policy) => Add(type => type == StartArray, policy);

    private JsonArrayBuilder<TContext> Obj(JsonObserverDelegate<TContext> policy) => Add(type => type == StartObject, policy);

    private JsonObserverDelegate<TContext> Build() => JsonObserverItem<TContext>.ApplyArrayPolicy([.. _policies], builderDefaultValuePolicy);

    private JsonArrayBuilder<TContext> Add(Func<JsonTokenType, bool> typeMatch, JsonObserverDelegate<TContext> policy)
    {
        _policies.Add(new JsonObserverItem<TContext>((int depth, ref PropertyPath _, JsonTokenType type) => (typeMatch(type), depth + 1), policy));
        return this;
    }
}
