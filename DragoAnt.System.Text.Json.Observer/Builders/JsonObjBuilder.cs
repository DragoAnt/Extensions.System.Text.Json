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

    internal static (JsonObserverDelegate<TContext> Delegate, RuleSet<TContext> Set) Build(JsonObjBuilder<TContext> builder) => builder.Build();

    private (JsonObserverDelegate<TContext>, RuleSet<TContext>) Build()
    {
        JsonObserverItem<TContext>[] items = [.. _policies];
        return (JsonObserverItem<TContext>.ApplyObjPolicy(items, _builderDefaultValuePolicy), new RuleSet<TContext>(false, items, _builderDefaultValuePolicy));
    }

    private JsonObjBuilder<TContext> AddAny(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverDelegate<TContext> policy, RuleInfo<TContext> info) =>
        Add((int depth, ref PropertyPath path, JsonTokenType _) => propNameMatch(depth, ref path), policy, info);

    private JsonObjBuilder<TContext> AddValue(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverDelegate<TContext> policy, RuleInfo<TContext> info) =>
        Add((int depth, ref PropertyPath path, JsonTokenType type) =>
        {
            var (success, propDepth) = propNameMatch(depth, ref path);

            if (!success || !type.IsValueToken())
            {
                return (false, 0);
            }

            return (true, propDepth);
        }, policy, info);

    private JsonObjBuilder<TContext> Add(JsonPropertyMatchDelegate propMatch, JsonObserverDelegate<TContext> policy, RuleInfo<TContext> info)
    {
        _policies.Add(new JsonObserverItem<TContext>(propMatch, policy) { Info = info });
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
            MaskWhole(JsonObserverItem<TContext>.ApplyStringPolicy(strategy, strategy.Constant), RuleText.Strategy("MaskStr", strategy.Constant));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadStr"/>
        public JsonObjBuilder<TContext> ReadStr(Action<string?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadStr(strategy, _builderDefaultValuePolicy), RuleText.ReadStr);

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskInt"/>
        public JsonObjBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyIntPolicy(strategy), "MaskInt(function)");

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadInt"/>
        public JsonObjBuilder<TContext> ReadInt(Action<int?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadInt(strategy, _builderDefaultValuePolicy), RuleText.ReadNumber("ReadInt"));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskLong"/>
        public JsonObjBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyLongPolicy(strategy), "MaskLong(function)");

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadLong"/>
        public JsonObjBuilder<TContext> ReadLong(Action<long?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadLong(strategy, _builderDefaultValuePolicy), RuleText.ReadNumber("ReadLong"));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskDecimal"/>
        public JsonObjBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyDecimalPolicy(strategy), "MaskDecimal(function)");

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadDecimal"/>
        public JsonObjBuilder<TContext> ReadDecimal(Action<decimal?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadDecimal(strategy, _builderDefaultValuePolicy), RuleText.ReadNumber("ReadDecimal"));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskBool"/>
        public JsonObjBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyBoolPolicy(strategy), "MaskBool(function)");

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadBool"/>
        public JsonObjBuilder<TContext> ReadBool(Action<bool?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadBool(strategy, _builderDefaultValuePolicy), RuleText.ReadBool);

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(Func{string, TContext, string})"/>
        public JsonObjBuilder<TContext> MaskAny(Func<string?, TContext, string?> strategy)
            => MaskAny((StringMaskingStrategy<TContext>)strategy);

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(StringMaskingStrategy{TContext})"/>
        public JsonObjBuilder<TContext> MaskAny(StringMaskingStrategy<TContext> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyAnyPolicy(strategy, strategy.Constant), RuleText.Strategy("MaskAny", strategy.Constant));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(MaskTag)"/>
        public JsonObjBuilder<TContext> MaskAny(MaskTag tag)
            => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag), RuleText.Tag(tag));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskRawValue"/>
        public JsonObjBuilder<TContext> MaskRawValue(Func<string?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyRawPolicy(strategy), "MaskRawValue(function)");

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadRaw"/>
        public JsonObjBuilder<TContext> ReadRaw(Action<string?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadRaw(strategy, _builderDefaultValuePolicy), RuleText.ReadRaw);

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskValue(JsonObserverValueDelegate{TContext})"/>
        public JsonObjBuilder<TContext> MaskValue(JsonObserverValueDelegate<TContext> policy) =>
            MaskValue(
                (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref PropertyPath propPath, JsonObserverValueDelegate<TContext> _) =>
                    policy(ref reader, writer, context, ref propPath));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskValue(JsonObserverDelegate{TContext})"/>
        public JsonObjBuilder<TContext> MaskValue(JsonObserverDelegate<TContext> policy)
            => _builder.AddValue(_propNameMatch.AbsoluteMatch, policy, Info(RuleText.CustomValue, JsonPathOutcome.Custom));

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.Unmasked"/>
        public JsonObjBuilder<TContext> Unmasked() =>
            _builder.AddValue(
                _propNameMatch.AbsoluteMatch,
                (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref PropertyPath propPath, JsonObserverValueDelegate<TContext> _) =>
                    JsonObserverValuePolicies<TContext>.BlockList(ref reader, writer, context, ref propPath),
                Info(RuleText.Unmasked, JsonPathOutcome.Unchanged));

        /// <summary>
        /// Rules for the matched property when its value is an object; a value of another type gets the next matching rule
        /// or the default policy.
        /// </summary>
        /// <param name="init">Adds the rules for the object's properties.</param>
        /// <param name="defaultValuePolicy">Policy for the object's values no rule matches; the enclosing one when <c>null</c>.</param>
        public JsonObjBuilder<TContext> Obj(
            Action<JsonObjBuilder<TContext>> init,
            JsonObserverValueDelegate<TContext>? defaultValuePolicy = null)
        {
            var (policy, set) = JsonObserverItem<TContext>.Obj(init, defaultValuePolicy ?? _builderDefaultValuePolicy);
            return Container(StartObject, policy, Info("Obj(...)", JsonPathOutcome.Unchanged, set));
        }

        /// <summary>
        /// Custom handling of the matched property when its value is an object.
        /// </summary>
        /// <param name="policy">Called with the reader on the object's start; it must write the object and move past it.</param>
        public JsonObjBuilder<TContext> Obj(JsonObserverDelegate<TContext> policy) =>
            Container(StartObject, policy, Info("Obj(custom rule)", JsonPathOutcome.Custom));

        /// <summary>
        /// Rules for the matched property when its value is an array; a value of another type gets the next matching rule
        /// or the default policy.
        /// </summary>
        /// <param name="init">Adds the rules for the array's items.</param>
        /// <param name="defaultValuePolicy">Policy for the array's values no rule matches; the enclosing one when <c>null</c>.</param>
        public JsonObjBuilder<TContext> Array(
            Action<JsonArrayBuilder<TContext>> init,
            JsonObserverValueDelegate<TContext>? defaultValuePolicy = null)
        {
            var (policy, set) = JsonObserverItem<TContext>.Array(init, defaultValuePolicy ?? _builderDefaultValuePolicy);
            return Container(StartArray, policy, Info("Array(...)", JsonPathOutcome.Unchanged, set));
        }

        /// <summary>
        /// Custom handling of the matched property when its value is an array.
        /// </summary>
        /// <param name="policy">Called with the reader on the array's start; it must write the array and move past it.</param>
        public JsonObjBuilder<TContext> Array(JsonObserverDelegate<TContext> policy) =>
            Container(StartArray, policy, Info("Array(custom rule)", JsonPathOutcome.Custom));

        internal JsonObjBuilder<TContext> MaskWhole(JsonObserverDelegate<TContext> policy, string action)
            => _builder.AddAny(_propNameMatch.AbsoluteMatch, policy, Info(action, JsonPathOutcome.Masked));

        private JsonObjBuilder<TContext> Read(JsonObserverDelegate<TContext> policy, string action)
            => _builder.AddValue(_propNameMatch.AbsoluteMatch, policy, Info(action, JsonPathOutcome.Read));

        private JsonObjBuilder<TContext> Container(JsonTokenType container, JsonObserverDelegate<TContext> policy, RuleInfo<TContext> info)
        {
            var match = _propNameMatch.AbsoluteMatch;
            return _builder.Add((int depth, ref PropertyPath path, JsonTokenType type) =>
            {
                var (success, nextDepth) = match(depth, ref path);

                if (!success || type != container)
                {
                    return (false, 0);
                }

                return (true, nextDepth);
            }, policy, info);
        }

        private RuleInfo<TContext> Info(string action, JsonPathOutcome outcome, RuleSet<TContext>? child = null) =>
            new(_propNameMatch.Describe(), action, outcome, child);
    }
}
