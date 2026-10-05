namespace DragoAnt.System.Text.Json.Observer.Builders;

/// <summary>
/// Rules of an any-depth policy (<see cref="JsonValuePolicy.AnyDepth(Action{JsonAnyDepthBuilder{NoContext}}, JsonValuePolicy{NoContext}?)"/>):
/// <see cref="Match"/> tests a property's own name and <see cref="Path"/> the last levels of its path, wherever it is
/// nested. The first rule that matches wins.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
public readonly struct JsonAnyDepthBuilder<TContext>
{
    private readonly List<JsonObserverItem<TContext>> _policies = [];
    private readonly JsonValuePolicy<TContext> _fallback;

    internal JsonAnyDepthBuilder(JsonValuePolicy<TContext> fallback)
    {
        _fallback = fallback;
    }

    /// <summary>
    /// Starts a rule for every property with this name, at any depth; finish it with a rule method.
    /// </summary>
    /// <param name="match">Property name test: a string for an exact name, or one of <see cref="Names"/>.</param>
    public PropertyMaskingStrategyBuilder Match(NameMatch match) =>
        new(this, new NamePathMatch([match], isPath: false));

    /// <summary>
    /// Starts a rule for the properties whose path ends with these names, at any depth; finish it with a rule method.
    /// </summary>
    /// <param name="path">Name tests for the last levels of the path, for example <c>"card", "number"</c> for any <c>…card.number</c>.</param>
    public PropertyMaskingStrategyBuilder Path(params NameMatch[] path) =>
        new(this, new NamePathMatch(path, isPath: true));

    /// <summary>
    /// Decides the comments of the values the rule just added matches, for the placements in <paramref name="kinds"/>,
    /// instead of the call's <see cref="ObserverOptions.Comments"/> policy. A kept comment of a masked value is still
    /// written masked unless <paramref name="rule"/> calls <see cref="CommentContext.Raw"/>.
    /// </summary>
    /// <param name="kinds">Placements the rule decides, for example <see cref="CommentKind.Any"/>.</param>
    /// <param name="rule">The comment rule, for example <see cref="CommentRules.Drop"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No rule was added yet.</exception>
    public JsonAnyDepthBuilder<TContext> Comment(CommentKind kinds, CommentRule rule)
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

    internal static ObserveRule<TContext> Build(JsonAnyDepthBuilder<TContext> builder) => builder.Build();

    internal static JsonObserverItem<TContext>[] BuildItems(JsonAnyDepthBuilder<TContext> builder) => [.. builder._policies];

    private JsonAnyDepthBuilder<TContext> AddAnyProp(JsonPropertyPathMatchDelegate propNameMatch, ObserveRule<TContext> policy, RuleInfo<TContext> info)
    {
        _policies.Add(new JsonObserverItem<TContext>((int depth, ref JsonWalk walk, JsonTokenType _) => propNameMatch(depth, ref walk), policy) { Info = info });
        return this;
    }

    private JsonAnyDepthBuilder<TContext> AddValueProp(JsonPropertyPathMatchDelegate propNameMatch, ObserveRule<TContext> policy, RuleInfo<TContext> info)
    {
        _policies.Add(new JsonObserverItem<TContext>(ValueMatch(propNameMatch), policy) { Info = info });
        return this;
    }

    private JsonAnyDepthBuilder<TContext> AddReadProp(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverItem<TContext>.ReadValue read, RuleInfo<TContext> info)
    {
        _policies.Add(JsonObserverItem<TContext>.Read(ValueMatch(propNameMatch), read, info));
        return this;
    }

    private static JsonPropertyMatchDelegate ValueMatch(JsonPropertyPathMatchDelegate propNameMatch) =>
        (int depth, ref JsonWalk walk, JsonTokenType type) =>
        {
            var (success, nextDepth) = propNameMatch(depth, ref walk);

            if (!success || !type.IsValueToken())
            {
                return (false, 0);
            }

            return (true, nextDepth);
        };

    private ObserveRule<TContext> Build() => JsonObserverItem<TContext>.ApplyValuePolicy([.. _policies], _fallback.Rule);

    /// <summary>
    /// A rule in progress: says what happens to the value of the matched property.
    /// </summary>
    public readonly ref struct PropertyMaskingStrategyBuilder
    {
        private readonly JsonAnyDepthBuilder<TContext> _builder;
        private readonly NamePathMatch _propNameMatch;

        internal PropertyMaskingStrategyBuilder(JsonAnyDepthBuilder<TContext> builder, NamePathMatch propNameMatch)
        {
            _builder = builder;
            _propNameMatch = propNameMatch;
        }

        /// <summary>
        /// Masks the whole value, whatever its JSON type, with the call's <see cref="ValueMaskStrategy"/>
        /// (<see cref="ObserverOptions.Strategy"/>), which receives <paramref name="tag"/>; a <c>null</c> value stays
        /// <c>null</c>, and an object or array is masked whole without being read.
        /// </summary>
        /// <param name="tag">How the value is masked, for example <see cref="MaskTag.Last4"/>.</param>
        public JsonAnyDepthBuilder<TContext> Mask(MaskTag tag)
            => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag), RuleText.Tag(tag), keepsNull: true);

        /// <summary>
        /// Masks the whole value, whatever its JSON type, with <paramref name="strategy"/> instead of the call's
        /// strategy; a <c>null</c> value stays <c>null</c>.
        /// </summary>
        /// <param name="strategy">Strategy of this rule.</param>
        /// <param name="tag">Tag handed to the strategy; <see cref="MaskTag.Full"/> by default.</param>
        /// <exception cref="ArgumentNullException"><paramref name="strategy"/> is <c>null</c>.</exception>
        public JsonAnyDepthBuilder<TContext> Mask(ValueMaskStrategy strategy, MaskTag tag = default)
            => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag, strategy ?? throw new ArgumentNullException(nameof(strategy))), RuleText.Strategy(tag), keepsNull: true);

        /// <summary>
        /// Masks the whole value, whatever its JSON type, with a replacement: a string arrives decoded, a number or
        /// boolean as its JSON literal (<c>"12.50"</c>, <c>"true"</c>), and an object or array is skipped unread and
        /// arrives as <c>null</c>. The replacement receives the whole value and its result is not cut by
        /// <see cref="ObserverOptions.MaxValueBytes"/>.
        /// </summary>
        /// <param name="strategy">
        /// Replacement: a constant string, a <see cref="global::System.Text.RegularExpressions.Regex"/> whose matches become <c>*</c>,
        /// or a function; a <c>null</c> result writes <c>null</c>.
        /// </param>
        /// <param name="nulls">Whether a <c>null</c> value stays <c>null</c> (the default) or is passed to the replacement.</param>
        public JsonAnyDepthBuilder<TContext> Mask(StringMaskingStrategy<TContext> strategy, MaskNulls nulls = MaskNulls.Keep)
            => MaskWhole(JsonObserverItem<TContext>.ApplyFunctionPolicy(strategy, strategy.Constant, nulls), RuleText.Function(strategy.Constant, nulls), nulls == MaskNulls.Keep);

        /// <summary>
        /// Masks the whole value with a function of the value and the context; see <see cref="Mask(StringMaskingStrategy{TContext}, MaskNulls)"/>.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        /// <param name="nulls">Whether a <c>null</c> value stays <c>null</c> (the default) or is passed to the function.</param>
        public JsonAnyDepthBuilder<TContext> Mask(Func<string?, TContext, string?> strategy, MaskNulls nulls = MaskNulls.Keep)
            => Mask((StringMaskingStrategy<TContext>)strategy, nulls);

        /// <summary>
        /// Masks the whole value with a function of the value; see <see cref="Mask(StringMaskingStrategy{TContext}, MaskNulls)"/>.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        /// <param name="nulls">Whether a <c>null</c> value stays <c>null</c> (the default) or is passed to the function.</param>
        public JsonAnyDepthBuilder<TContext> Mask(Func<string?, string?> strategy, MaskNulls nulls = MaskNulls.Keep)
            => Mask(StringMaskingStrategy<TContext>.From(strategy), nulls);

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
        public JsonAnyDepthBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyIntPolicy(strategy), "MaskInt(function)");

        /// <summary>
        /// Hands a number or <c>null</c> value to <paramref name="strategy"/>; a number that does not fit <see cref="int"/>
        /// arrives as <c>null</c>, and a value of another type is not read. Reading does not decide the output: the default
        /// policy writes the value unless <see cref="ReadRuleBuilder.Unmasked"/> or a mask method is chained.
        /// </summary>
        /// <param name="strategy">Receives the value and the context.</param>
        public ReadRuleBuilder ReadInt(Action<int?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadInt(strategy), RuleText.ReadNumber("ReadInt"));

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives the number
        /// when it fits <see cref="long"/>, and <c>null</c> for anything else.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonAnyDepthBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyLongPolicy(strategy), "MaskLong(function)");

        /// <summary>
        /// Hands a number or <c>null</c> value to <paramref name="strategy"/>; a number that does not fit <see cref="long"/>
        /// arrives as <c>null</c>, and a value of another type is not read. Reading does not decide the output.
        /// </summary>
        /// <param name="strategy">Receives the value and the context.</param>
        public ReadRuleBuilder ReadLong(Action<long?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadLong(strategy), RuleText.ReadNumber("ReadLong"));

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives the number
        /// when it fits <see cref="decimal"/>, and <c>null</c> for anything else.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonAnyDepthBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyDecimalPolicy(strategy), "MaskDecimal(function)");

        /// <summary>
        /// Hands a number or <c>null</c> value to <paramref name="strategy"/>; a number out of the <see cref="decimal"/> range
        /// arrives as <c>null</c>, and a value of another type is not read. Reading does not decide the output.
        /// </summary>
        /// <param name="strategy">Receives the value, parsed with the invariant culture, and the context.</param>
        public ReadRuleBuilder ReadDecimal(Action<decimal?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadDecimal(strategy), RuleText.ReadNumber("ReadDecimal"));

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives
        /// <c>true</c> or <c>false</c>, and <c>null</c> for anything else, including an object or array, which is skipped unread.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonAnyDepthBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyBoolPolicy(strategy), "MaskBool(function)");

        /// <summary>
        /// Hands a boolean or <c>null</c> value to <paramref name="strategy"/>; a value of another type is not read.
        /// Reading does not decide the output.
        /// </summary>
        /// <param name="strategy">Receives the value and the context.</param>
        public ReadRuleBuilder ReadBool(Action<bool?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadBool(strategy), RuleText.ReadBool);

        /// <summary>
        /// Hands a string, number, boolean or <c>null</c> value to <paramref name="strategy"/> as its raw JSON text; an object
        /// or array is not read. Reading does not decide the output.
        /// </summary>
        /// <param name="strategy">Receives the raw text (a string without quotes, escapes kept) and the context.</param>
        public ReadRuleBuilder ReadRaw(Action<string?, TContext> strategy)
            => Read(JsonObserverItem<TContext>.ReadRaw(strategy), RuleText.ReadRaw);

        /// <summary>
        /// Writes the value of the matched property unchanged. Applies to strings, numbers, booleans and <c>null</c>;
        /// an object or array gets the next matching rule or the default policy.
        /// </summary>
        public JsonAnyDepthBuilder<TContext> Unmasked() =>
            Value((
                    ref Utf8JsonReader reader,
                    JsonWriter writer,
                    TContext context,
                    int depth,
                    ref JsonWalk walk,
                    ValueRule<TContext> _) =>
                BuiltInPolicies<TContext>.BlockList(ref reader, writer, context, ref walk), RuleText.Unmasked, PathOutcome.Unchanged);

        /// <summary>
        /// Custom rule for a string, number, boolean or <c>null</c> value of the matched property; an object or array
        /// gets the next matching rule or the default policy.
        /// </summary>
        /// <param name="rule">Called with the reader on the value; it must write exactly one value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <c>null</c>.</exception>
        public JsonAnyDepthBuilder<TContext> MaskValue(JsonValueRule<TContext> rule)
            => Value(JsonObserverItem<TContext>.ApplyCustomRule(rule ?? throw new ArgumentNullException(nameof(rule))), RuleText.CustomValue, PathOutcome.Custom);

        internal JsonAnyDepthBuilder<TContext> MaskWhole(ObserveRule<TContext> policy, string action, bool keepsNull = false)
            => _builder.AddAnyProp(Match, policy, Info(action, PathOutcome.Masked) with { KeepsNull = keepsNull });

        private ReadRuleBuilder Read(JsonObserverItem<TContext>.ReadValue read, string action)
            => new(this, _builder.AddReadProp(Match, read, Info(action, PathOutcome.Read)));

        private JsonAnyDepthBuilder<TContext> Value(ObserveRule<TContext> policy, string action, PathOutcome outcome)
            => _builder.AddValueProp(Match, policy, Info(action, outcome));

        private JsonPropertyPathMatchDelegate Match => _propNameMatch.RelativeMatch;

        private RuleInfo<TContext> Info(string action, PathOutcome outcome) => new(_propNameMatch.Describe(), action, outcome);
    }

    /// <summary>
    /// A read rule just added. A read rule does not decide what is written: the default policy writes the value unless
    /// <see cref="Unmasked"/> or a mask method is chained here, which applies to the same match. <see cref="Match"/> and
    /// <see cref="Path"/> start the next rule.
    /// </summary>
    public readonly ref struct ReadRuleBuilder
    {
        private readonly PropertyMaskingStrategyBuilder _rule;
        private readonly JsonAnyDepthBuilder<TContext> _builder;

        internal ReadRuleBuilder(PropertyMaskingStrategyBuilder rule, JsonAnyDepthBuilder<TContext> builder)
        {
            _rule = rule;
            _builder = builder;
        }

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.Match"/>
        public PropertyMaskingStrategyBuilder Match(NameMatch match) => _builder.Match(match);

        /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.Path"/>
        public PropertyMaskingStrategyBuilder Path(params NameMatch[] path) => _builder.Path(path);

        /// <summary>
        /// Writes the read value unchanged instead of through the default policy.
        /// </summary>
        public JsonAnyDepthBuilder<TContext> Unmasked() => _rule.Unmasked();

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.Mask(MaskTag)"/>
        public JsonAnyDepthBuilder<TContext> Mask(MaskTag tag) => _rule.Mask(tag);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.Mask(ValueMaskStrategy, MaskTag)"/>
        public JsonAnyDepthBuilder<TContext> Mask(ValueMaskStrategy strategy, MaskTag tag = default) => _rule.Mask(strategy, tag);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.Mask(StringMaskingStrategy{TContext}, MaskNulls)"/>
        public JsonAnyDepthBuilder<TContext> Mask(StringMaskingStrategy<TContext> strategy, MaskNulls nulls = MaskNulls.Keep) => _rule.Mask(strategy, nulls);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.Mask(Func{string, TContext, string}, MaskNulls)"/>
        public JsonAnyDepthBuilder<TContext> Mask(Func<string?, TContext, string?> strategy, MaskNulls nulls = MaskNulls.Keep) => _rule.Mask(strategy, nulls);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.Mask(Func{string, string}, MaskNulls)"/>
        public JsonAnyDepthBuilder<TContext> Mask(Func<string?, string?> strategy, MaskNulls nulls = MaskNulls.Keep) => _rule.Mask(strategy, nulls);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskInt"/>
        public JsonAnyDepthBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy) => _rule.MaskInt(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskLong"/>
        public JsonAnyDepthBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy) => _rule.MaskLong(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskDecimal"/>
        public JsonAnyDepthBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy) => _rule.MaskDecimal(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskBool"/>
        public JsonAnyDepthBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy) => _rule.MaskBool(strategy);

        /// <inheritdoc cref="PropertyMaskingStrategyBuilder.MaskValue"/>
        public JsonAnyDepthBuilder<TContext> MaskValue(JsonValueRule<TContext> rule) => _rule.MaskValue(rule);

        /// <summary>
        /// The rules added so far, to keep adding to them.
        /// </summary>
        /// <param name="rule">The read rule just added.</param>
        public static implicit operator JsonAnyDepthBuilder<TContext>(ReadRuleBuilder rule) => rule._builder;
    }
}
