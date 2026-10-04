using DragoAnt.System.Text.Json.Observer.Strategies;
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
    private readonly JsonObserverValueDelegate<TContext>? _builderDefaultValuePolicy;

    internal JsonArrayBuilder(JsonObserverValueDelegate<TContext>? builderDefaultValuePolicy)
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
        JsonObserverValueDelegate<TContext>? defaultValuePolicy = null)
    {
        var (policy, set) = JsonObserverItem<TContext>.Obj(init, defaultValuePolicy ?? _builderDefaultValuePolicy);
        return Add(type => type == StartObject, policy, new RuleInfo<TContext>("object item", "Obj(...)", JsonPathOutcome.Unchanged, set));
    }

    /// <summary>
    /// Rules for the items that are arrays.
    /// </summary>
    /// <param name="init">Adds the rules for each nested array's items.</param>
    /// <param name="defaultValuePolicy">Policy for the nested arrays' values no rule matches; the enclosing one when <c>null</c>.</param>
    public JsonArrayBuilder<TContext> Array(
        Action<JsonArrayBuilder<TContext>> init,
        JsonObserverValueDelegate<TContext>? defaultValuePolicy = null)
    {
        var (policy, set) = JsonObserverItem<TContext>.Array(init, defaultValuePolicy ?? _builderDefaultValuePolicy);
        return Add(type => type == StartArray, policy, new RuleInfo<TContext>("array item", "Array(...)", JsonPathOutcome.Unchanged, set));
    }

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskStr(Func{string, TContext, string})"/>
    public JsonArrayBuilder<TContext> MaskStr(Func<string?, TContext, string?> strategy)
        => MaskStr((StringMaskingStrategy<TContext>)strategy);

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskStr(StringMaskingStrategy{TContext})"/>
    public JsonArrayBuilder<TContext> MaskStr(StringMaskingStrategy<TContext> strategy) =>
        MaskWhole(JsonObserverItem<TContext>.ApplyStringPolicy(strategy, strategy.Constant), RuleText.Strategy("MaskStr", strategy.Constant));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadStr"/>
    public JsonArrayBuilder<TContext> ReadStr(Action<string?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadStr(strategy, _builderDefaultValuePolicy), RuleText.ReadStr);

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskInt"/>
    public JsonArrayBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyIntPolicy(strategy), "MaskInt(function)");

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadInt"/>
    public JsonArrayBuilder<TContext> ReadInt(Action<int?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadInt(strategy, _builderDefaultValuePolicy), RuleText.ReadNumber("ReadInt"));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskLong"/>
    public JsonArrayBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyLongPolicy(strategy), "MaskLong(function)");

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadLong"/>
    public JsonArrayBuilder<TContext> ReadLong(Action<long?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadLong(strategy, _builderDefaultValuePolicy), RuleText.ReadNumber("ReadLong"));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskDecimal"/>
    public JsonArrayBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyDecimalPolicy(strategy), "MaskDecimal(function)");

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadDecimal"/>
    public JsonArrayBuilder<TContext> ReadDecimal(Action<decimal?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadDecimal(strategy, _builderDefaultValuePolicy), RuleText.ReadNumber("ReadDecimal"));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskBool"/>
    public JsonArrayBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyBoolPolicy(strategy), "MaskBool(function)");

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadBool"/>
    public JsonArrayBuilder<TContext> ReadBool(Action<bool?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadBool(strategy, _builderDefaultValuePolicy), RuleText.ReadBool);

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(Func{string, TContext, string})"/>
    public JsonArrayBuilder<TContext> MaskAny(Func<string?, TContext, string?> strategy)
        => MaskAny((StringMaskingStrategy<TContext>)strategy);

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(StringMaskingStrategy{TContext})"/>
    public JsonArrayBuilder<TContext> MaskAny(StringMaskingStrategy<TContext> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyAnyPolicy(strategy, strategy.Constant), RuleText.Strategy("MaskAny", strategy.Constant));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(MaskTag)"/>
    public JsonArrayBuilder<TContext> MaskAny(MaskTag tag)
        => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag), RuleText.Tag(tag));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskRawValue"/>
    public JsonArrayBuilder<TContext> MaskRawValue(Func<string?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyRawPolicy(strategy), "MaskRawValue(function)");

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadRaw"/>
    public JsonArrayBuilder<TContext> ReadRaw(Action<string?, TContext> strategy)
        => Read(JsonObserverItem<TContext>.ReadRaw(strategy, _builderDefaultValuePolicy), RuleText.ReadRaw);

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskValue(JsonObserverValueDelegate{TContext})"/>
    public JsonArrayBuilder<TContext> MaskValue(JsonObserverValueDelegate<TContext> policy) =>
        MaskValue(
            (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref PropertyPath propPath, JsonObserverValueDelegate<TContext> _) =>
                policy(ref reader, writer, context, ref propPath));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskValue(JsonObserverDelegate{TContext})"/>
    public JsonArrayBuilder<TContext> MaskValue(JsonObserverDelegate<TContext> policy)
        => Add(type => type.IsValueToken(), policy, new RuleInfo<TContext>(ValueItem, RuleText.CustomValue, JsonPathOutcome.Custom));

    /// <summary>
    /// Writes the string, number, boolean and <c>null</c> items unchanged; object and array items get the next rule or the default policy.
    /// </summary>
    public JsonArrayBuilder<TContext> Unmasked() =>
        Add(
            type => type.IsValueToken(),
            (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int _, ref PropertyPath propPath, JsonObserverValueDelegate<TContext> _) =>
                JsonObserverValuePolicies<TContext>.BlockList(ref reader, writer, context, ref propPath),
            new RuleInfo<TContext>(ValueItem, RuleText.Unmasked, JsonPathOutcome.Unchanged));

    internal JsonArrayBuilder<TContext> MaskWhole(JsonObserverDelegate<TContext> policy, string action) =>
        Add(_ => true, policy, new RuleInfo<TContext>(AnyItem, action, JsonPathOutcome.Masked));

    internal static (JsonObserverDelegate<TContext> Delegate, RuleSet<TContext> Set) Build(JsonArrayBuilder<TContext> builder) => builder.Build();

    private JsonArrayBuilder<TContext> Read(JsonObserverDelegate<TContext> policy, string action) =>
        Add(type => type.IsValueToken(), policy, new RuleInfo<TContext>(ValueItem, action, JsonPathOutcome.Read));

    private (JsonObserverDelegate<TContext>, RuleSet<TContext>) Build()
    {
        JsonObserverItem<TContext>[] items = [.. _policies];
        return (JsonObserverItem<TContext>.ApplyArrayPolicy(items, _builderDefaultValuePolicy), new RuleSet<TContext>(true, items, _builderDefaultValuePolicy));
    }

    private JsonArrayBuilder<TContext> Add(Func<JsonTokenType, bool> typeMatch, JsonObserverDelegate<TContext> policy, RuleInfo<TContext> info)
    {
        _policies.Add(new JsonObserverItem<TContext>((int _, ref PropertyPath _, JsonTokenType type) => (typeMatch(type), 1), policy) { Info = info });
        return this;
    }
}
