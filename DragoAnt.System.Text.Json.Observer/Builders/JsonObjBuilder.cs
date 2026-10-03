using DragoAnt.System.Text.Json.Observer.Strategies;
using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer.Builders;

/// <summary>
/// Rules for the properties of one JSON object. A property path given to <see cref="Match(PropMatchingStrategy[])"/>
/// starts at this object; the first rule that matches a property wins.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
public readonly struct JsonObjBuilder<TContext>
{
    private readonly List<JsonObserverItem<TContext>> _policies = [];
    private readonly JsonObserverValueDelegate<TContext>? _builderDefaultValuePolicy;

    internal JsonObjBuilder(JsonObserverValueDelegate<TContext>? builderDefaultValuePolicy)
    {
        _builderDefaultValuePolicy = builderDefaultValuePolicy;
    }

    /// <summary>
    /// Starts a rule for the property this matches; finish it with a rule method.
    /// </summary>
    /// <param name="match">Property name test: a string for an exact, case-insensitive name, or one of <see cref="PropMatches"/>.</param>
    public PropertyMaskingStrategyBuilder Match(PropMatchingStrategy match) =>
        new(this, new PropertyPathMatch([match]), _builderDefaultValuePolicy);

    /// <summary>
    /// Starts a rule for the nested property path this matches, one name test per level; finish it with a rule method.
    /// </summary>
    /// <param name="match">Name tests from this object down, for example <c>"card", "number"</c> for <c>card.number</c>.</param>
    public PropertyMaskingStrategyBuilder Match(params PropMatchingStrategy[] match) =>
        new(this, new PropertyPathMatch(match), _builderDefaultValuePolicy);

    internal static JsonObserverDelegate<TContext> Build(JsonObjBuilder<TContext> builder) => builder.Build();
    private JsonObserverDelegate<TContext> Build() => JsonObserverItem<TContext>.ApplyObjPolicy([.. _policies], _builderDefaultValuePolicy);

    private JsonObjBuilder<TContext> AddAny(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverDelegate<TContext> policy) =>
        Add((int depth, ref PropertyPath path, JsonTokenType _) => propNameMatch(depth, ref path), policy);

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
    /// A rule in progress: says what happens to the value of the matched property.
    /// </summary>
    public readonly ref struct PropertyMaskingStrategyBuilder
    {
        private readonly JsonObjBuilder<TContext> _builder;
        private readonly PropertyPathMatch _propNameMatch;
        private readonly JsonObserverValueDelegate<TContext>? _builderDefaultValuePolicy;

        internal PropertyMaskingStrategyBuilder(
            JsonObjBuilder<TContext> builder,
            PropertyPathMatch propNameMatch,
            JsonObserverValueDelegate<TContext>? builderDefaultValuePolicy)
        {
            _builder = builder;
            _propNameMatch = propNameMatch;
            _builderDefaultValuePolicy = builderDefaultValuePolicy;
        }

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskStr(Func{string, TContext, string})"/>
        public JsonObjBuilder<TContext> MaskStr(Func<string?, TContext, string?> strategy)
            => MaskStr((StringMaskingStrategy<TContext>)strategy);

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskStr(StringMaskingStrategy{TContext})"/>
        public JsonObjBuilder<TContext> MaskStr(StringMaskingStrategy<TContext> strategy) =>
            MaskWhole(JsonObserverItem<TContext>.ApplyStringPolicy(strategy, strategy.Constant));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadStr"/>
        public JsonObjBuilder<TContext> ReadStr(Action<string?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadStr(strategy, _builderDefaultValuePolicy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskInt"/>
        public JsonObjBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyIntPolicy(strategy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadInt"/>
        public JsonObjBuilder<TContext> ReadInt(Action<int?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadInt(strategy, _builderDefaultValuePolicy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskLong"/>
        public JsonObjBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyLongPolicy(strategy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadLong"/>
        public JsonObjBuilder<TContext> ReadLong(Action<long?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadLong(strategy, _builderDefaultValuePolicy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskDecimal"/>
        public JsonObjBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyDecimalPolicy(strategy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadDecimal"/>
        public JsonObjBuilder<TContext> ReadDecimal(Action<decimal?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadDecimal(strategy, _builderDefaultValuePolicy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskBool"/>
        public JsonObjBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyBoolPolicy(strategy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadBool"/>
        public JsonObjBuilder<TContext> ReadBool(Action<bool?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadBool(strategy, _builderDefaultValuePolicy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(Func{string, TContext, string})"/>
        public JsonObjBuilder<TContext> MaskAny(Func<string?, TContext, string?> strategy)
            => MaskAny((StringMaskingStrategy<TContext>)strategy);

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(StringMaskingStrategy{TContext})"/>
        public JsonObjBuilder<TContext> MaskAny(StringMaskingStrategy<TContext> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyAnyPolicy(strategy, strategy.Constant));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(MaskTag)"/>
        public JsonObjBuilder<TContext> MaskAny(MaskTag tag)
            => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskRawValue"/>
        public JsonObjBuilder<TContext> MaskRawValue(Func<string?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyRawPolicy(strategy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadRaw"/>
        public JsonObjBuilder<TContext> ReadRaw(Action<string?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadRaw(strategy, _builderDefaultValuePolicy));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskValue(JsonObserverValueDelegate{TContext})"/>
        public JsonObjBuilder<TContext> MaskValue(JsonObserverValueDelegate<TContext> policy) =>
            MaskValue(
                (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref PropertyPath propPath, JsonObserverValueDelegate<TContext> _) =>
                    policy(ref reader, writer, context, ref propPath));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskValue(JsonObserverDelegate{TContext})"/>
        public JsonObjBuilder<TContext> MaskValue(JsonObserverDelegate<TContext> policy)
            => _builder.AddValue(_propNameMatch.AbsoluteMatch, policy);

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.Unmasked"/>
        public JsonObjBuilder<TContext> Unmasked() =>
            _builder.AddValue(
                _propNameMatch.AbsoluteMatch,
                (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref PropertyPath propPath, JsonObserverValueDelegate<TContext> _) =>
                    JsonObserverValuePolicies<TContext>.BlockList(ref reader, writer, context, ref propPath));

        /// <summary>
        /// Rules for the matched property when its value is an object; a value of another type gets the next matching rule
        /// or the default policy.
        /// </summary>
        /// <param name="init">Adds the rules for the object's properties.</param>
        /// <param name="defaultValuePolicy">Policy for the object's values no rule matches; the enclosing one when <c>null</c>.</param>
        public JsonObjBuilder<TContext> Obj(
            Action<JsonObjBuilder<TContext>> init,
            JsonObserverValueDelegate<TContext>? defaultValuePolicy = null) =>
            Obj(JsonObserverItem<TContext>.Obj(init, defaultValuePolicy ?? _builderDefaultValuePolicy));

        /// <summary>
        /// Custom handling of the matched property when its value is an object.
        /// </summary>
        /// <param name="policy">Called with the reader on the object's start; it must write the object and move past it.</param>
        public JsonObjBuilder<TContext> Obj(JsonObserverDelegate<TContext> policy)
        {
            var match = _propNameMatch.AbsoluteMatch;
            return _builder.Add((int depth, ref PropertyPath path, JsonTokenType type) =>
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
        /// Rules for the matched property when its value is an array; a value of another type gets the next matching rule
        /// or the default policy.
        /// </summary>
        /// <param name="init">Adds the rules for the array's items.</param>
        /// <param name="defaultValuePolicy">Policy for the array's values no rule matches; the enclosing one when <c>null</c>.</param>
        public JsonObjBuilder<TContext> Array(
            Action<JsonArrayBuilder<TContext>> init,
            JsonObserverValueDelegate<TContext>? defaultValuePolicy = null) =>
            Array(JsonObserverItem<TContext>.Array(init, defaultValuePolicy ?? _builderDefaultValuePolicy));

        /// <summary>
        /// Custom handling of the matched property when its value is an array.
        /// </summary>
        /// <param name="policy">Called with the reader on the array's start; it must write the array and move past it.</param>
        public JsonObjBuilder<TContext> Array(JsonObserverDelegate<TContext> policy)
        {
            var match = _propNameMatch.AbsoluteMatch;
            return _builder.Add((int depth, ref PropertyPath path, JsonTokenType type) =>
            {
                var (success, nextDepth) = match(depth, ref path);

                if (!success || type != StartArray)
                {
                    return (false, 0);
                }
                return (true, nextDepth);
            }, policy);
        }

        internal JsonObjBuilder<TContext> MaskWhole(JsonObserverDelegate<TContext> policy)
            => _builder.AddAny(_propNameMatch.AbsoluteMatch, policy);
    }
}
