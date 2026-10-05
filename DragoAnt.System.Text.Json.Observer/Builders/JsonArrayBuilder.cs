using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer.Builders;

/// <summary>
/// Rules for the items of one JSON array. Every rule applies to every item; the first rule that accepts an item wins.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
public readonly struct JsonArrayBuilder<TContext>
{
    private const string AnyItem = "any item";
    private const string ValueItem = "string, number, boolean or null item";

    private readonly List<JsonObserverItem<TContext>> _policies = [];
    private readonly JsonValuePolicy<TContext>? _builderDefaultValuePolicy;

    internal JsonArrayBuilder(JsonValuePolicy<TContext>? builderDefaultValuePolicy)
    {
        _builderDefaultValuePolicy = builderDefaultValuePolicy;
    }

    /// <summary>
    /// Rules for the items that are objects.
    /// </summary>
    /// <param name="init">Adds the rules for each object's properties.</param>
    /// <param name="defaultValuePolicy">Policy for the objects' values no rule matches; the enclosing one when <c>null</c>.</param>
    public JsonArrayBuilder<TContext> Obj(
        Action<JsonObjBuilder<TContext>> init,
        JsonValuePolicy<TContext>? defaultValuePolicy = null)
    {
        var (policy, set) = JsonObserverItem<TContext>.Obj(init, defaultValuePolicy ?? _builderDefaultValuePolicy);
        return Add(type => type == StartObject, policy, new RuleInfo<TContext>("object item", "Obj(...)", PathOutcome.Unchanged, set));
    }

    /// <summary>
    /// Rules for the items that are arrays.
    /// </summary>
    /// <param name="init">Adds the rules for each nested array's items.</param>
    /// <param name="defaultValuePolicy">Policy for the nested arrays' values no rule matches; the enclosing one when <c>null</c>.</param>
    public JsonArrayBuilder<TContext> Array(
        Action<JsonArrayBuilder<TContext>> init,
        JsonValuePolicy<TContext>? defaultValuePolicy = null)
    {
        var (policy, set) = JsonObserverItem<TContext>.Array(init, defaultValuePolicy ?? _builderDefaultValuePolicy);
        return Add(type => type == StartArray, policy, new RuleInfo<TContext>("array item", "Array(...)", PathOutcome.Unchanged, set));
    }

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(MaskTag)"/>
    public JsonArrayBuilder<TContext> Mask(MaskTag tag)
        => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag), RuleText.Tag(tag), keepsNull: true);

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(ValueMaskStrategy, MaskTag)"/>
    public JsonArrayBuilder<TContext> Mask(ValueMaskStrategy strategy, MaskTag tag = default)
        => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag, strategy ?? throw new ArgumentNullException(nameof(strategy))), RuleText.Strategy(tag), keepsNull: true);

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(StringMaskingStrategy{TContext}, MaskNulls)"/>
    public JsonArrayBuilder<TContext> Mask(StringMaskingStrategy<TContext> strategy, MaskNulls nulls = MaskNulls.Keep)
        => MaskWhole(JsonObserverItem<TContext>.ApplyFunctionPolicy(strategy, strategy.Constant, nulls), RuleText.Function(strategy.Constant, nulls), nulls == MaskNulls.Keep);

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(Func{string, TContext, string}, MaskNulls)"/>
    public JsonArrayBuilder<TContext> Mask(Func<string?, TContext, string?> strategy, MaskNulls nulls = MaskNulls.Keep)
        => Mask((StringMaskingStrategy<TContext>)strategy, nulls);

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.Mask(Func{string, string}, MaskNulls)"/>
    public JsonArrayBuilder<TContext> Mask(Func<string?, string?> strategy, MaskNulls nulls = MaskNulls.Keep)
        => Mask(StringMaskingStrategy<TContext>.From(strategy), nulls);

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadStr"/>
    public JsonArrayBuilder<TContext> ReadStr(Action<string?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadStr(strategy), RuleText.ReadStr);

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskInt"/>
    public JsonArrayBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyIntPolicy(strategy), "MaskInt(function)");

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadInt"/>
    public JsonArrayBuilder<TContext> ReadInt(Action<int?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadInt(strategy), RuleText.ReadNumber("ReadInt"));

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskLong"/>
    public JsonArrayBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyLongPolicy(strategy), "MaskLong(function)");

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadLong"/>
    public JsonArrayBuilder<TContext> ReadLong(Action<long?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadLong(strategy), RuleText.ReadNumber("ReadLong"));

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskDecimal"/>
    public JsonArrayBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyDecimalPolicy(strategy), "MaskDecimal(function)");

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadDecimal"/>
    public JsonArrayBuilder<TContext> ReadDecimal(Action<decimal?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadDecimal(strategy), RuleText.ReadNumber("ReadDecimal"));

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskBool"/>
    public JsonArrayBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyBoolPolicy(strategy), "MaskBool(function)");

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadBool"/>
    public JsonArrayBuilder<TContext> ReadBool(Action<bool?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadBool(strategy), RuleText.ReadBool);

    /// <inheritdoc cref="JsonAnyDepthBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadRaw"/>
    public JsonArrayBuilder<TContext> ReadRaw(Action<string?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadRaw(strategy), RuleText.ReadRaw);

    /// <summary>
    /// Custom rule for the string, number, boolean and <c>null</c> items; object and array items get the next rule or the default policy.
    /// </summary>
    /// <param name="rule">Called with the reader on the item; it must write exactly one value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <c>null</c>.</exception>
    public JsonArrayBuilder<TContext> MaskValue(JsonValueRule<TContext> rule)
        => Add(type => type.IsValueToken(), JsonObserverItem<TContext>.ApplyCustomRule(rule ?? throw new ArgumentNullException(nameof(rule))),
            new RuleInfo<TContext>(ValueItem, RuleText.CustomValue, PathOutcome.Custom));

    /// <summary>
    /// Writes the string, number, boolean and <c>null</c> items unchanged; object and array items get the next rule or the default policy.
    /// </summary>
    public JsonArrayBuilder<TContext> Unmasked() =>
        Add(
            type => type.IsValueToken(),
            (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int _, ref JsonWalk walk, ValueRule<TContext> _) =>
                BuiltInPolicies<TContext>.BlockList(ref reader, writer, context, ref walk),
            new RuleInfo<TContext>(ValueItem, RuleText.Unmasked, PathOutcome.Unchanged));

    internal JsonArrayBuilder<TContext> MaskWhole(ObserveRule<TContext> policy, string action, bool keepsNull = false) =>
        Add(_ => true, policy, new RuleInfo<TContext>(AnyItem, action, PathOutcome.Masked, KeepsNull: keepsNull));

    /// <summary>
    /// Decides the comments of the values the rule just added matches, for the placements in <paramref name="kinds"/>,
    /// instead of the call's <see cref="ObserverOptions.Comments"/> policy. A kept comment of a masked value is still
    /// written masked unless <paramref name="rule"/> calls <see cref="CommentContext.Raw"/>.
    /// </summary>
    /// <param name="kinds">Placements the rule decides, for example <see cref="CommentKind.Any"/>.</param>
    /// <param name="rule">The comment rule, for example <see cref="CommentRules.Drop"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">No rule was added yet.</exception>
    public JsonArrayBuilder<TContext> Comment(CommentKind kinds, CommentRule rule)
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

    internal static (ObserveRule<TContext> Delegate, RuleSet<TContext> Set) Build(JsonArrayBuilder<TContext> builder) => builder.Build();

    private JsonArrayBuilder<TContext> Read(JsonObserverItem<TContext>.ReadValue read, string action)
    {
        _policies.Add(JsonObserverItem<TContext>.Read(
            (int _, ref JsonWalk _, JsonTokenType type) => (type.IsValueToken(), 1),
            read,
            new RuleInfo<TContext>(ValueItem, action, PathOutcome.Read)));
        return this;
    }

    private (ObserveRule<TContext>, RuleSet<TContext>) Build()
    {
        JsonObserverItem<TContext>[] items = [.. _policies];
        return (JsonObserverItem<TContext>.ApplyArrayPolicy(items, _builderDefaultValuePolicy?.Rule), new RuleSet<TContext>(true, items, _builderDefaultValuePolicy));
    }

    private JsonArrayBuilder<TContext> Add(Func<JsonTokenType, bool> typeMatch, ObserveRule<TContext> policy, RuleInfo<TContext> info)
    {
        _policies.Add(new JsonObserverItem<TContext>((int _, ref JsonWalk _, JsonTokenType type) => (typeMatch(type), 1), policy) { Info = info });
        return this;
    }
}
