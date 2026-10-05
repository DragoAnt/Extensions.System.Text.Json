using DragoAnt.System.Text.Json.Observer.Strategies;

namespace DragoAnt.System.Text.Json.Observer.Builders;

/// <summary>
/// Rules of a relative policy (<see cref="JsonObserverValuePolicies{TContext}.Relative"/>): a property path given to
/// <see cref="Match"/> matches the end of a property's path at any depth. The first rule that matches wins.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
public readonly struct JsonValuePolicyBuilder<TContext>
{
    private readonly bool _relative;
    private readonly List<JsonObserverItem<TContext>> _policies = [];
    private readonly JsonObserverValueDelegate<TContext>? _builderDefaultValuePolicy;

    internal JsonValuePolicyBuilder(bool relative, JsonObserverValueDelegate<TContext>? builderDefaultValuePolicy)
    {
        _relative = relative;
        _builderDefaultValuePolicy = builderDefaultValuePolicy;
    }

    /// <summary>
    /// Starts a rule for the properties whose path ends with these names, at any depth; finish it with a rule method.
    /// </summary>
    /// <param name="match">
    /// Name tests for the last levels of the path, for example <c>"card", "number"</c> for any <c>…card.number</c>:
    /// a string for an exact, case-insensitive name, or one of <see cref="PropMatches"/>.
    /// </param>
    public PropertyMaskingStrategyBuilder Match(params PropMatchingStrategy[] match) =>
        new(this, new PropertyPathMatch(match), _builderDefaultValuePolicy);

    internal static JsonObserverDelegate<TContext> Build(JsonValuePolicyBuilder<TContext> builder) => builder.Build();

    internal static JsonObserverItem<TContext>[] BuildItems(JsonValuePolicyBuilder<TContext> builder) => [.. builder._policies];

    private JsonValuePolicyBuilder<TContext> AddAnyProp(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverDelegate<TContext> policy, RuleInfo<TContext> info)
    {
        _policies.Add(new JsonObserverItem<TContext>((int depth, ref PropertyPath path, JsonTokenType _) => propNameMatch(depth, ref path), policy) { Info = info });
        return this;
    }

    private JsonValuePolicyBuilder<TContext> AddValueProp(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverDelegate<TContext> policy, RuleInfo<TContext> info)
    {
        _policies.Add(new JsonObserverItem<TContext>(ValueMatch(propNameMatch), policy) { Info = info });
        return this;
    }

    private JsonValuePolicyBuilder<TContext> AddReadProp(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverItem<TContext>.ReadValue read, RuleInfo<TContext> info)
    {
        _policies.Add(JsonObserverItem<TContext>.Read(ValueMatch(propNameMatch), read, info));
        return this;
    }

    private static JsonPropertyMatchDelegate ValueMatch(JsonPropertyPathMatchDelegate propNameMatch) =>
        (int depth, ref PropertyPath path, JsonTokenType type) =>
        {
            var (success, nextDepth) = propNameMatch(depth, ref path);

            if (!success || !type.IsValueToken())
            {
                return (false, 0);
            }

            return (true, nextDepth);
        };

    private JsonObserverDelegate<TContext> Build() => JsonObserverItem<TContext>.ApplyValuePolicy([.. _policies], _builderDefaultValuePolicy);

    /// <summary>
    /// A rule in progress: says what happens to the value of the matched property.
    /// </summary>
    public readonly ref struct PropertyMaskingStrategyBuilder
    {
        private readonly JsonValuePolicyBuilder<TContext> _builder;
        private readonly PropertyPathMatch _propNameMatch;
        private readonly JsonObserverValueDelegate<TContext>? _builderDefaultValuePolicy;

        internal PropertyMaskingStrategyBuilder(
            JsonValuePolicyBuilder<TContext> builder,
            PropertyPathMatch propNameMatch,
            JsonObserverValueDelegate<TContext>? builderDefaultValuePolicy)
        {
            _builder = builder;
            _propNameMatch = propNameMatch;
            _builderDefaultValuePolicy = builderDefaultValuePolicy;
        }

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type: a string arrives decoded,
        /// a number or boolean as its JSON literal (<c>"12.50"</c>, <c>"true"</c>), <c>null</c> as <c>null</c>, and an object
        /// or array is skipped unread and arrives as <c>null</c>. A value longer than
        /// <see cref="JsonObserverOptions.MaxValueBytes"/> arrives cut to that length.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskStr(Func<string?, TContext, string?> strategy)
            => MaskStr((StringMaskingStrategy<TContext>)strategy);

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type: a string arrives decoded,
        /// a number or boolean as its JSON literal (<c>"12.50"</c>, <c>"true"</c>), <c>null</c> as <c>null</c>, and an object
        /// or array is skipped unread and arrives as <c>null</c>. A value longer than
        /// <see cref="JsonObserverOptions.MaxValueBytes"/> arrives cut to that length.
        /// </summary>
        /// <param name="strategy">
        /// Replacement: a constant string, a <see cref="global::System.Text.RegularExpressions.Regex"/> whose matches become <c>*</c>,
        /// or a function; a <c>null</c> result writes <c>null</c>.
        /// </param>
        public JsonValuePolicyBuilder<TContext> MaskStr(StringMaskingStrategy<TContext> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyStringPolicy(strategy, strategy.Constant), RuleText.Strategy("MaskStr", strategy.Constant));

        /// <summary>
        /// Hands a string or <c>null</c> value to <paramref name="strategy"/>; a value of another type is not read.
        /// Reading does not decide the output: the default policy writes the value unless <see cref="ReadRuleBuilder.Unmasked"/>
        /// or a mask method is chained, or another rule writes the same match.
        /// </summary>
        /// <param name="strategy">Receives the decoded value and the context.</param>
        public ReadRuleBuilder ReadStr(Action<string?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadStr(strategy), RuleText.ReadStr);

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives the number
        /// when it fits <see cref="int"/>, and <c>null</c> for anything else: another type, a fractional or too large number,
        /// <c>null</c>, or an object or array, which is skipped unread.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyIntPolicy(strategy), "MaskInt(function)");

        /// <summary>
        /// Hands a number or <c>null</c> value to <paramref name="strategy"/>; a number that does not fit <see cref="int"/>
        /// arrives as <c>null</c>, and a value of another type is not read. Reading does not decide the output: the default policy writes the value unless <see cref="ReadRuleBuilder.Unmasked"/>
        /// or a mask method is chained, or another rule writes the same match.
        /// </summary>
        /// <param name="strategy">Receives the value and the context.</param>
        public ReadRuleBuilder ReadInt(Action<int?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadInt(strategy), RuleText.ReadNumber("ReadInt"));

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives the number
        /// when it fits <see cref="long"/>, and <c>null</c> for anything else: another type, a fractional or too large number,
        /// <c>null</c>, or an object or array, which is skipped unread.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyLongPolicy(strategy), "MaskLong(function)");

        /// <summary>
        /// Hands a number or <c>null</c> value to <paramref name="strategy"/>; a number that does not fit <see cref="long"/>
        /// arrives as <c>null</c>, and a value of another type is not read. Reading does not decide the output: the default policy writes the value unless <see cref="ReadRuleBuilder.Unmasked"/>
        /// or a mask method is chained, or another rule writes the same match.
        /// </summary>
        /// <param name="strategy">Receives the value and the context.</param>
        public ReadRuleBuilder ReadLong(Action<long?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadLong(strategy), RuleText.ReadNumber("ReadLong"));

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives the number
        /// when it fits <see cref="decimal"/>, and <c>null</c> for anything else: another type, a number out of range,
        /// <c>null</c>, or an object or array, which is skipped unread.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyDecimalPolicy(strategy), "MaskDecimal(function)");

        /// <summary>
        /// Hands a number or <c>null</c> value to <paramref name="strategy"/>; a number out of the <see cref="decimal"/> range
        /// arrives as <c>null</c>, and a value of another type is not read. Reading does not decide the output: the default policy writes the value unless <see cref="ReadRuleBuilder.Unmasked"/>
        /// or a mask method is chained, or another rule writes the same match.
        /// </summary>
        /// <param name="strategy">Receives the value, parsed with the invariant culture, and the context.</param>
        public ReadRuleBuilder ReadDecimal(Action<decimal?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadDecimal(strategy), RuleText.ReadNumber("ReadDecimal"));

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives
        /// <c>true</c> or <c>false</c>, and <c>null</c> for anything else, including an object or array, which is skipped unread.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyBoolPolicy(strategy), "MaskBool(function)");

        /// <summary>
        /// Hands a boolean or <c>null</c> value to <paramref name="strategy"/>; a value of another type is not read.
        /// Reading does not decide the output: the default policy writes the value unless <see cref="ReadRuleBuilder.Unmasked"/>
        /// or a mask method is chained, or another rule writes the same match.
        /// </summary>
        /// <param name="strategy">Receives the value and the context.</param>
        public ReadRuleBuilder ReadBool(Action<bool?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadBool(strategy), RuleText.ReadBool);

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type: a string arrives decoded,
        /// a number or boolean as its JSON literal, and an object or array is skipped unread and arrives as <c>null</c>.
        /// A <c>null</c> value stays <c>null</c> without calling the strategy. A value longer than
        /// <see cref="JsonObserverOptions.MaxValueBytes"/> arrives cut to that length.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskAny(Func<string?, TContext, string?> strategy)
            => MaskAny((StringMaskingStrategy<TContext>)strategy);

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type: a string arrives decoded,
        /// a number or boolean as its JSON literal, and an object or array is skipped unread and arrives as <c>null</c>.
        /// A <c>null</c> value stays <c>null</c> without calling the strategy. A value longer than
        /// <see cref="JsonObserverOptions.MaxValueBytes"/> arrives cut to that length.
        /// </summary>
        /// <param name="strategy">
        /// Replacement: a constant string, a <see cref="global::System.Text.RegularExpressions.Regex"/> whose matches become <c>*</c>,
        /// or a function; a <c>null</c> result writes <c>null</c>.
        /// </param>
        public JsonValuePolicyBuilder<TContext> MaskAny(StringMaskingStrategy<TContext> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyAnyPolicy(strategy, strategy.Constant), RuleText.Strategy("MaskAny", strategy.Constant));

        /// <summary>
        /// Masks the whole value, whatever its JSON type, with the <see cref="Utf8MaskStrategy"/> of the call
        /// (<see cref="JsonObserverOptions.MaskStrategy"/>), which receives <paramref name="tag"/>; a <c>null</c> value stays <c>null</c>.
        /// </summary>
        /// <param name="tag">How the value is masked, for example <see cref="MaskTag.Last4"/>.</param>
        public JsonValuePolicyBuilder<TContext> MaskAny(MaskTag tag)
            => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag), RuleText.Tag(tag));

        /// <summary>
        /// Like <see cref="MaskStr(Func{string, TContext, string})"/>, but a string arrives as its raw JSON text,
        /// escape sequences included and without quotes.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskRawValue(Func<string?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyRawPolicy(strategy), "MaskRawValue(function)");

        /// <summary>
        /// Hands a string, number, boolean or <c>null</c> value to <paramref name="strategy"/> as its raw JSON text; an object
        /// or array is not read. Reading does not decide the output: the default policy writes the value unless <see cref="ReadRuleBuilder.Unmasked"/>
        /// or a mask method is chained, or another rule writes the same match.
        /// </summary>
        /// <param name="strategy">Receives the raw text (a string without quotes, escapes kept) and the context.</param>
        public ReadRuleBuilder ReadRaw(Action<string?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadRaw(strategy), RuleText.ReadRaw);

        /// <summary>
        /// Writes the value of the matched property unchanged. Applies to strings, numbers, booleans and <c>null</c>;
        /// an object or array gets the next matching rule or the default policy.
        /// </summary>
        public JsonValuePolicyBuilder<TContext> Unmasked() =>
            Value((
                    ref Utf8JsonReader reader,
                    JsonWriter writer,
                    TContext context,
                    int depth,
                    ref PropertyPath propPath,
                    JsonObserverValueDelegate<TContext> _) =>
                JsonObserverValuePolicies<TContext>.BlockList(ref reader, writer, context, ref propPath), RuleText.Unmasked, JsonPathOutcome.Unchanged);

        /// <summary>
        /// Custom rule for a string, number, boolean or <c>null</c> value of the matched property; an object or array
        /// gets the next matching rule or the default policy.
        /// </summary>
        /// <param name="policy">Called with the reader on the value; it must write exactly one value.</param>
        public JsonValuePolicyBuilder<TContext> MaskValue(JsonObserverValueDelegate<TContext> policy) =>
            MaskValue((
                    ref Utf8JsonReader reader,
                    JsonWriter writer,
                    TContext context,
                    int depth,
                    ref PropertyPath propPath,
                    JsonObserverValueDelegate<TContext> _) =>
                policy(ref reader, writer, context, ref propPath));

        /// <summary>
        /// Custom rule for a string, number, boolean or <c>null</c> value of the matched property; an object or array
        /// gets the next matching rule or the default policy.
        /// </summary>
        /// <param name="policy">Called with the reader on the value; it must write exactly one value.</param>
        public JsonValuePolicyBuilder<TContext> MaskValue(JsonObserverDelegate<TContext> policy)
            => Value(policy, RuleText.CustomValue, JsonPathOutcome.Custom);

        internal JsonValuePolicyBuilder<TContext> MaskWhole(JsonObserverDelegate<TContext> policy, string action)
            => _builder.AddAnyProp(Match, policy, Info(action, JsonPathOutcome.Masked));

        private ReadRuleBuilder Read(JsonObserverItem<TContext>.ReadValue read, string action)
            => new(this, _builder.AddReadProp(Match, read, Info(action, JsonPathOutcome.Read)));

        private JsonValuePolicyBuilder<TContext> Value(JsonObserverDelegate<TContext> policy, string action, JsonPathOutcome outcome)
            => _builder.AddValueProp(Match, policy, Info(action, outcome));

        private JsonPropertyPathMatchDelegate Match => _builder._relative ? _propNameMatch.RelativeMatch : _propNameMatch.AbsoluteMatch;

        private RuleInfo<TContext> Info(string action, JsonPathOutcome outcome) => new(_propNameMatch.Describe(), action, outcome);
    }

    /// <summary>
    /// A read rule just added. A read rule does not decide what is written: the default policy writes the value unless
    /// <see cref="Unmasked"/> or a mask method is chained here, which applies to the same match. <see cref="Match"/>
    /// starts the next rule.
    /// </summary>
    public readonly ref struct ReadRuleBuilder
    {
        private readonly PropertyMaskingStrategyBuilder _rule;
        private readonly JsonValuePolicyBuilder<TContext> _builder;

        internal ReadRuleBuilder(PropertyMaskingStrategyBuilder rule, JsonValuePolicyBuilder<TContext> builder)
        {
            _rule = rule;
            _builder = builder;
        }

        /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.Match"/>
        public PropertyMaskingStrategyBuilder Match(params PropMatchingStrategy[] match) => _builder.Match(match);

        /// <summary>
        /// Writes the read value unchanged instead of through the default policy.
        /// </summary>
        public JsonValuePolicyBuilder<TContext> Unmasked() => _rule.Unmasked();

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskStr(Func{string, TContext, string})"/>
        public JsonValuePolicyBuilder<TContext> MaskStr(Func<string?, TContext, string?> strategy) => _rule.MaskStr(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskStr(StringMaskingStrategy{TContext})"/>
        public JsonValuePolicyBuilder<TContext> MaskStr(StringMaskingStrategy<TContext> strategy) => _rule.MaskStr(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskInt"/>
        public JsonValuePolicyBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy) => _rule.MaskInt(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskLong"/>
        public JsonValuePolicyBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy) => _rule.MaskLong(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskDecimal"/>
        public JsonValuePolicyBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy) => _rule.MaskDecimal(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskBool"/>
        public JsonValuePolicyBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy) => _rule.MaskBool(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskAny(Func{string, TContext, string})"/>
        public JsonValuePolicyBuilder<TContext> MaskAny(Func<string?, TContext, string?> strategy) => _rule.MaskAny(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskAny(StringMaskingStrategy{TContext})"/>
        public JsonValuePolicyBuilder<TContext> MaskAny(StringMaskingStrategy<TContext> strategy) => _rule.MaskAny(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskAny(MaskTag)"/>
        public JsonValuePolicyBuilder<TContext> MaskAny(MaskTag tag) => _rule.MaskAny(tag);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskRawValue"/>
        public JsonValuePolicyBuilder<TContext> MaskRawValue(Func<string?, TContext, string?> strategy) => _rule.MaskRawValue(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskValue(JsonObserverValueDelegate{TContext})"/>
        public JsonValuePolicyBuilder<TContext> MaskValue(JsonObserverValueDelegate<TContext> policy) => _rule.MaskValue(policy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskValue(JsonObserverDelegate{TContext})"/>
        public JsonValuePolicyBuilder<TContext> MaskValue(JsonObserverDelegate<TContext> policy) => _rule.MaskValue(policy);

        /// <summary>
        /// The rules added so far, to keep adding to them.
        /// </summary>
        /// <param name="rule">The read rule just added.</param>
        public static implicit operator JsonValuePolicyBuilder<TContext>(ReadRuleBuilder rule) => rule._builder;
    }
}
