using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer.Builders;

/// <summary>
/// Rules for the properties of one JSON object. <see cref="Match"/> tests a property of this object,
/// <see cref="Path"/> a nested property below it; the first rule that matches a property wins.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
public readonly struct JsonObjBuilder<TContext>
{
    private readonly List<JsonObserverItem<TContext>> _policies = [];
    private readonly JsonValuePolicy<TContext>? _builderDefaultValuePolicy;

    internal JsonObjBuilder(JsonValuePolicy<TContext>? builderDefaultValuePolicy)
    {
        _builderDefaultValuePolicy = builderDefaultValuePolicy;
    }

    /// <summary>
    /// Starts a rule for the property this matches; finish it with a rule method.
    /// </summary>
    /// <param name="match">Property name test: a string for an exact name, or one of <see cref="Names"/>.</param>
    public PropertyMaskingStrategyBuilder Match(NameMatch match) =>
        new(this, new NamePathMatch([match], isPath: false), _builderDefaultValuePolicy);

    /// <summary>
    /// Starts a rule for the nested property path this matches, one name test per level; finish it with a rule method.
    /// </summary>
    /// <param name="path">Name tests from this object down, for example <c>"card", "number"</c> for <c>card.number</c>.</param>
    public PropertyMaskingStrategyBuilder Path(params NameMatch[] path) =>
        new(this, new NamePathMatch(path, isPath: true), _builderDefaultValuePolicy);

    /// <summary>
    /// Decides the comments of the values the rule just added matches, for the placements in <paramref name="kinds"/>,
    /// instead of the call's <see cref="ObserverOptions.Comments"/> policy. A kept comment of a masked value is still
    /// written masked unless <paramref name="rule"/> calls <see cref="CommentContext.Raw"/>.
    /// </summary>
    /// <param name="kinds">Placements the rule decides, for example <see cref="CommentKind.Any"/>.</param>
    /// <param name="rule">The comment rule, for example <see cref="CommentRules.Drop"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No rule was added yet.</exception>
    public JsonObjBuilder<TContext> Comment(CommentKind kinds, CommentRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (_policies.Count == 0)
        {
            throw new InvalidOperationException("Add a rule before its comment rule.");
        }

        var last = _policies[^1];
        last.CommentKinds = kinds;
        last.CommentRule = rule;
        return this;
    }

    internal static (ObserveRule<TContext> Delegate, RuleSet<TContext> Set) Build(JsonObjBuilder<TContext> builder) => builder.Build();

    private (ObserveRule<TContext>, RuleSet<TContext>) Build()
    {
        JsonObserverItem<TContext>[] items = [.. _policies];
        return (JsonObserverItem<TContext>.ApplyObjPolicy(items, _builderDefaultValuePolicy?.Rule), new RuleSet<TContext>(false, items, _builderDefaultValuePolicy));
    }

    private JsonObjBuilder<TContext> AddAny(JsonPropertyPathMatchDelegate propNameMatch, ObserveRule<TContext> policy, RuleInfo<TContext> info) =>
        Add((int depth, ref JsonWalk walk, JsonTokenType _) => propNameMatch(depth, ref walk), policy, info);

    private JsonObjBuilder<TContext> AddValue(JsonPropertyPathMatchDelegate propNameMatch, ObserveRule<TContext> policy, RuleInfo<TContext> info) =>
        Add(ValueMatch(propNameMatch), policy, info);

    private JsonObjBuilder<TContext> AddRead(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverItem<TContext>.ReadValue read, RuleInfo<TContext> info)
    {
        _policies.Add(JsonObserverItem<TContext>.Read(ValueMatch(propNameMatch), read, info));
        return this;
    }

    private static JsonPropertyMatchDelegate ValueMatch(JsonPropertyPathMatchDelegate propNameMatch) =>
        (int depth, ref JsonWalk walk, JsonTokenType type) =>
        {
            var (success, propDepth) = propNameMatch(depth, ref walk);

            if (!success || !type.IsValueToken())
            {
                return (false, 0);
            }

            return (true, propDepth);
        };

    private JsonObjBuilder<TContext> Add(JsonPropertyMatchDelegate propMatch, ObserveRule<TContext> policy, RuleInfo<TContext> info)
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
        private readonly NamePathMatch _propNameMatch;
        private readonly JsonValuePolicy<TContext>? _builderDefaultValuePolicy;

        internal PropertyMaskingStrategyBuilder(
            JsonObjBuilder<TContext> builder,
            NamePathMatch propNameMatch,
            JsonValuePolicy<TContext>? builderDefaultValuePolicy)
        {
            _builder = builder;
            _propNameMatch = propNameMatch;
            _builderDefaultValuePolicy = builderDefaultValuePolicy;
        }

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(MaskTag)"/>
        public JsonObjBuilder<TContext> Mask(MaskTag tag)
            => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag), RuleText.Tag(tag), keepsNull: true);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(ValueMaskStrategy, MaskTag)"/>
        public JsonObjBuilder<TContext> Mask(ValueMaskStrategy strategy, MaskTag tag = default)
            => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag, strategy ?? throw new ArgumentNullException(nameof(strategy))), RuleText.Strategy(tag), keepsNull: true);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(StringMaskingStrategy{TContext}, MaskNulls)"/>
        public JsonObjBuilder<TContext> Mask(StringMaskingStrategy<TContext> strategy, MaskNulls nulls = MaskNulls.Keep)
            => MaskWhole(JsonObserverItem<TContext>.ApplyFunctionPolicy(strategy, strategy.Constant, nulls), RuleText.Function(strategy.Constant, nulls), nulls == MaskNulls.Keep);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(Func{string, TContext, string}, MaskNulls)"/>
        public JsonObjBuilder<TContext> Mask(Func<string?, TContext, string?> strategy, MaskNulls nulls = MaskNulls.Keep)
            => Mask((StringMaskingStrategy<TContext>)strategy, nulls);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(Func{string, string}, MaskNulls)"/>
        public JsonObjBuilder<TContext> Mask(Func<string?, string?> strategy, MaskNulls nulls = MaskNulls.Keep)
            => Mask(StringMaskingStrategy<TContext>.From(strategy), nulls);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadStr"/>
        public ReadRuleBuilder ReadStr(Action<string?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadStr(strategy), RuleText.ReadStr);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskInt"/>
        public JsonObjBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyIntPolicy(strategy), "MaskInt(function)");

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadInt"/>
        public ReadRuleBuilder ReadInt(Action<int?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadInt(strategy), RuleText.ReadNumber("ReadInt"));

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskLong"/>
        public JsonObjBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyLongPolicy(strategy), "MaskLong(function)");

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadLong"/>
        public ReadRuleBuilder ReadLong(Action<long?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadLong(strategy), RuleText.ReadNumber("ReadLong"));

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskDecimal"/>
        public JsonObjBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyDecimalPolicy(strategy), "MaskDecimal(function)");

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadDecimal"/>
        public ReadRuleBuilder ReadDecimal(Action<decimal?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadDecimal(strategy), RuleText.ReadNumber("ReadDecimal"));

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskBool"/>
        public JsonObjBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyBoolPolicy(strategy), "MaskBool(function)");

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadBool"/>
        public ReadRuleBuilder ReadBool(Action<bool?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadBool(strategy), RuleText.ReadBool);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadRaw"/>
        public ReadRuleBuilder ReadRaw(Action<string?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadRaw(strategy), RuleText.ReadRaw);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskValue"/>
        public JsonObjBuilder<TContext> MaskValue(JsonValueRule<TContext> rule)
            => _builder.AddValue(_propNameMatch.AbsoluteMatch, JsonObserverItem<TContext>.ApplyCustomRule(rule ?? throw new ArgumentNullException(nameof(rule))), Info(RuleText.CustomValue, PathOutcome.Custom));

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Unmasked"/>
        public JsonObjBuilder<TContext> Unmasked() =>
            _builder.AddValue(
                _propNameMatch.AbsoluteMatch,
                (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref JsonWalk walk, ValueRule<TContext> _) =>
                    BuiltInPolicies<TContext>.BlockList(ref reader, writer, context, ref walk),
                Info(RuleText.Unmasked, PathOutcome.Unchanged));

        /// <summary>
        /// Rules for the matched property when its value is an object; a value of another type gets the next matching rule
        /// or the default policy.
        /// </summary>
        /// <param name="init">Adds the rules for the object's properties.</param>
        /// <param name="defaultValuePolicy">Policy for the object's values no rule matches; the enclosing one when <c>null</c>.</param>
        public JsonObjBuilder<TContext> Obj(
            Action<JsonObjBuilder<TContext>> init,
            JsonValuePolicy<TContext>? defaultValuePolicy = null)
        {
            var (policy, set) = JsonObserverItem<TContext>.Obj(init, defaultValuePolicy ?? _builderDefaultValuePolicy);
            return Container(StartObject, policy, Info("Obj(...)", PathOutcome.Unchanged, set));
        }

        /// <summary>
        /// Custom handling of the matched property when its value is an object.
        /// </summary>
        /// <param name="rule">Called with the reader on the object's start; it must write the object and leave the reader on its end.</param>
        public JsonObjBuilder<TContext> Obj(JsonValueRule<TContext> rule) =>
            Container(StartObject, JsonObserverItem<TContext>.ApplyCustomRule(rule ?? throw new ArgumentNullException(nameof(rule))), Info("Obj(custom rule)", PathOutcome.Custom));

        /// <summary>
        /// Rules for the matched property when its value is an array; a value of another type gets the next matching rule
        /// or the default policy.
        /// </summary>
        /// <param name="init">Adds the rules for the array's items.</param>
        /// <param name="defaultValuePolicy">Policy for the array's values no rule matches; the enclosing one when <c>null</c>.</param>
        public JsonObjBuilder<TContext> Array(
            Action<JsonArrayBuilder<TContext>> init,
            JsonValuePolicy<TContext>? defaultValuePolicy = null)
        {
            var (policy, set) = JsonObserverItem<TContext>.Array(init, defaultValuePolicy ?? _builderDefaultValuePolicy);
            return Container(StartArray, policy, Info("Array(...)", PathOutcome.Unchanged, set));
        }

        /// <summary>
        /// Custom handling of the matched property when its value is an array.
        /// </summary>
        /// <param name="rule">Called with the reader on the array's start; it must write the array and leave the reader on its end.</param>
        public JsonObjBuilder<TContext> Array(JsonValueRule<TContext> rule) =>
            Container(StartArray, JsonObserverItem<TContext>.ApplyCustomRule(rule ?? throw new ArgumentNullException(nameof(rule))), Info("Array(custom rule)", PathOutcome.Custom));

        internal JsonObjBuilder<TContext> MaskWhole(ObserveRule<TContext> policy, string action, bool keepsNull = false)
            => _builder.AddAny(_propNameMatch.AbsoluteMatch, policy, Info(action, PathOutcome.Masked) with { KeepsNull = keepsNull });

        private ReadRuleBuilder Read(JsonObserverItem<TContext>.ReadValue read, string action)
            => new(this, _builder.AddRead(_propNameMatch.AbsoluteMatch, read, Info(action, PathOutcome.Read)));

        private JsonObjBuilder<TContext> Container(JsonTokenType container, ObserveRule<TContext> policy, RuleInfo<TContext> info)
        {
            var match = _propNameMatch.AbsoluteMatch;
            return _builder.Add((int depth, ref JsonWalk walk, JsonTokenType type) =>
            {
                var (success, nextDepth) = match(depth, ref walk);

                if (!success || type != container)
                {
                    return (false, 0);
                }

                return (true, nextDepth);
            }, policy, info);
        }

        private RuleInfo<TContext> Info(string action, PathOutcome outcome, RuleSet<TContext>? child = null) =>
            new(_propNameMatch.Describe(), action, outcome, child);
    }

    /// <summary>
    /// A read rule just added. A read rule does not decide what is written: the default policy writes the value unless
    /// <see cref="Unmasked"/> or a mask method is chained here, which applies to the same match. <see cref="Match"/> and
    /// <see cref="Path"/> start the next rule.
    /// </summary>
    public readonly ref struct ReadRuleBuilder
    {
        private readonly PropertyMaskingStrategyBuilder _rule;
        private readonly JsonObjBuilder<TContext> _builder;

        internal ReadRuleBuilder(PropertyMaskingStrategyBuilder rule, JsonObjBuilder<TContext> builder)
        {
            _rule = rule;
            _builder = builder;
        }

        /// <inheritdoc cref="JsonObjBuilder{TContext}.Match"/>
        public PropertyMaskingStrategyBuilder Match(NameMatch match) => _builder.Match(match);

        /// <inheritdoc cref="JsonObjBuilder{TContext}.Path"/>
        public PropertyMaskingStrategyBuilder Path(params NameMatch[] path) => _builder.Path(path);

        /// <summary>
        /// Writes the read value unchanged instead of through the default policy.
        /// </summary>
        public JsonObjBuilder<TContext> Unmasked() => _rule.Unmasked();

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(MaskTag)"/>
        public JsonObjBuilder<TContext> Mask(MaskTag tag) => _rule.Mask(tag);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(ValueMaskStrategy, MaskTag)"/>
        public JsonObjBuilder<TContext> Mask(ValueMaskStrategy strategy, MaskTag tag = default) => _rule.Mask(strategy, tag);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(StringMaskingStrategy{TContext}, MaskNulls)"/>
        public JsonObjBuilder<TContext> Mask(StringMaskingStrategy<TContext> strategy, MaskNulls nulls = MaskNulls.Keep) => _rule.Mask(strategy, nulls);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(Func{string, TContext, string}, MaskNulls)"/>
        public JsonObjBuilder<TContext> Mask(Func<string?, TContext, string?> strategy, MaskNulls nulls = MaskNulls.Keep) => _rule.Mask(strategy, nulls);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(Func{string, string}, MaskNulls)"/>
        public JsonObjBuilder<TContext> Mask(Func<string?, string?> strategy, MaskNulls nulls = MaskNulls.Keep) => _rule.Mask(strategy, nulls);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskInt"/>
        public JsonObjBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy) => _rule.MaskInt(strategy);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskLong"/>
        public JsonObjBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy) => _rule.MaskLong(strategy);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskDecimal"/>
        public JsonObjBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy) => _rule.MaskDecimal(strategy);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskBool"/>
        public JsonObjBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy) => _rule.MaskBool(strategy);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskValue"/>
        public JsonObjBuilder<TContext> MaskValue(JsonValueRule<TContext> rule) => _rule.MaskValue(rule);

        /// <summary>
        /// The rules added so far, to keep adding to them.
        /// </summary>
        /// <param name="rule">The read rule just added.</param>
        public static implicit operator JsonObjBuilder<TContext>(ReadRuleBuilder rule) => rule._builder;
    }
}
