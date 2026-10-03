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

    private JsonValuePolicyBuilder<TContext> AddAnyProp(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverDelegate<TContext> policy)
    {
        _policies.Add(new JsonObserverItem<TContext>((int depth, ref PropertyPath path, JsonTokenType _) => propNameMatch(depth, ref path), policy));
        return this;
    }

    private JsonValuePolicyBuilder<TContext> AddValueProp(JsonPropertyPathMatchDelegate propNameMatch, JsonObserverDelegate<TContext> policy)
    {
        var item = new JsonObserverItem<TContext>((int depth, ref PropertyPath path, JsonTokenType type) =>
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
            => MaskWhole(JsonObserverItem<TContext>.ApplyStringPolicy(strategy, strategy.Constant));

        /// <summary>
        /// Hands a string or <c>null</c> value to <paramref name="strategy"/> and writes it unchanged.
        /// A value of another type is not read and gets the default policy.
        /// </summary>
        /// <param name="strategy">Receives the decoded value and the context.</param>
        public JsonValuePolicyBuilder<TContext> ReadStr(Action<string?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadStr(strategy, _builderDefaultValuePolicy));

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives the number
        /// when it fits <see cref="int"/>, and <c>null</c> for anything else: another type, a fractional or too large number,
        /// <c>null</c>, or an object or array, which is skipped unread.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyIntPolicy(strategy));

        /// <summary>
        /// Hands a number or <c>null</c> value to <paramref name="strategy"/> and writes it unchanged; a number that does not
        /// fit <see cref="int"/> arrives as <c>null</c>. A value of another type is not read and gets the default policy.
        /// </summary>
        /// <param name="strategy">Receives the value and the context.</param>
        public JsonValuePolicyBuilder<TContext> ReadInt(Action<int?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadInt(strategy, _builderDefaultValuePolicy));

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives the number
        /// when it fits <see cref="long"/>, and <c>null</c> for anything else: another type, a fractional or too large number,
        /// <c>null</c>, or an object or array, which is skipped unread.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyLongPolicy(strategy));

        /// <summary>
        /// Hands a number or <c>null</c> value to <paramref name="strategy"/> and writes it unchanged; a number that does not
        /// fit <see cref="long"/> arrives as <c>null</c>. A value of another type is not read and gets the default policy.
        /// </summary>
        /// <param name="strategy">Receives the value and the context.</param>
        public JsonValuePolicyBuilder<TContext> ReadLong(Action<long?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadLong(strategy, _builderDefaultValuePolicy));

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives the number
        /// when it fits <see cref="decimal"/>, and <c>null</c> for anything else: another type, a number out of range,
        /// <c>null</c>, or an object or array, which is skipped unread.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyDecimalPolicy(strategy));

        /// <summary>
        /// Hands a number or <c>null</c> value to <paramref name="strategy"/> and writes it unchanged; a number out of the
        /// <see cref="decimal"/> range arrives as <c>null</c>. A value of another type is not read and gets the default policy.
        /// </summary>
        /// <param name="strategy">Receives the value, parsed with the invariant culture, and the context.</param>
        public JsonValuePolicyBuilder<TContext> ReadDecimal(Action<decimal?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadDecimal(strategy, _builderDefaultValuePolicy));

        /// <summary>
        /// Masks the whole value with <paramref name="strategy"/>, whatever its JSON type. The strategy receives
        /// <c>true</c> or <c>false</c>, and <c>null</c> for anything else, including an object or array, which is skipped unread.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyBoolPolicy(strategy));

        /// <summary>
        /// Hands a boolean or <c>null</c> value to <paramref name="strategy"/> and writes it unchanged.
        /// A value of another type is not read and gets the default policy.
        /// </summary>
        /// <param name="strategy">Receives the value and the context.</param>
        public JsonValuePolicyBuilder<TContext> ReadBool(Action<bool?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadBool(strategy, _builderDefaultValuePolicy));

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
            => MaskWhole(JsonObserverItem<TContext>.ApplyAnyPolicy(strategy, strategy.Constant));

        /// <summary>
        /// Masks the whole value, whatever its JSON type, with the <see cref="Utf8MaskStrategy"/> of the call
        /// (<see cref="JsonObserverOptions.MaskStrategy"/>), which receives <paramref name="tag"/>; a <c>null</c> value stays <c>null</c>.
        /// </summary>
        /// <param name="tag">How the value is masked, for example <see cref="MaskTag.Last4"/>.</param>
        public JsonValuePolicyBuilder<TContext> MaskAny(MaskTag tag)
            => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag));

        /// <summary>
        /// Like <see cref="MaskStr(Func{string, TContext, string})"/>, but a string arrives as its raw JSON text,
        /// escape sequences included and without quotes.
        /// </summary>
        /// <param name="strategy">Returns the replacement string; <c>null</c> writes <c>null</c>.</param>
        public JsonValuePolicyBuilder<TContext> MaskRawValue(Func<string?, TContext, string?> strategy)
            => MaskWhole(JsonObserverItem<TContext>.ApplyRawPolicy(strategy));

        /// <summary>
        /// Hands a string, number, boolean or <c>null</c> value to <paramref name="strategy"/> as its raw JSON text and writes
        /// it unchanged. An object or array is not read and gets the default policy.
        /// </summary>
        /// <param name="strategy">Receives the raw text (a string without quotes, escapes kept) and the context.</param>
        public JsonValuePolicyBuilder<TContext> ReadRaw(Action<string?, TContext> strategy)
            => MaskValue(JsonObserverItem<TContext>.ReadRaw(strategy, _builderDefaultValuePolicy));

        /// <summary>
        /// Writes the value of the matched property unchanged. Applies to strings, numbers, booleans and <c>null</c>;
        /// an object or array gets the next matching rule or the default policy.
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
            => _builder.AddValueProp(_builder._relative ? _propNameMatch.RelativeMatch : _propNameMatch.AbsoluteMatch, policy);

        internal JsonValuePolicyBuilder<TContext> MaskWhole(JsonObserverDelegate<TContext> policy)
            => _builder.AddAnyProp(_builder._relative ? _propNameMatch.RelativeMatch : _propNameMatch.AbsoluteMatch, policy);
    }
}
