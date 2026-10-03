using DragoAnt.System.Text.Json.Observer.Strategies;
using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer.Builders;

/// <summary>
/// Rules for the items of one JSON array. Every rule applies to every item; the first rule that accepts an item wins.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
public readonly struct JsonArrayBuilder<TContext>
{
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
        JsonObserverValueDelegate<TContext>? defaultValuePolicy = null) =>
        Obj(JsonObserverItem<TContext>.Obj(init, defaultValuePolicy ?? _builderDefaultValuePolicy));

    /// <summary>
    /// Rules for the items that are arrays.
    /// </summary>
    /// <param name="init">Adds the rules for each nested array's items.</param>
    /// <param name="defaultValuePolicy">Policy for the nested arrays' values no rule matches; the enclosing one when <c>null</c>.</param>
    public JsonArrayBuilder<TContext> Array(
        Action<JsonArrayBuilder<TContext>> init,
        JsonObserverValueDelegate<TContext>? defaultValuePolicy = null) =>
        Array(JsonObserverItem<TContext>.Array(init, defaultValuePolicy ?? _builderDefaultValuePolicy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskStr(Func{string, TContext, string})"/>
    public JsonArrayBuilder<TContext> MaskStr(Func<string?, TContext, string?> strategy)
        => MaskStr((StringMaskingStrategy<TContext>)strategy);

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskStr(StringMaskingStrategy{TContext})"/>
    public JsonArrayBuilder<TContext> MaskStr(StringMaskingStrategy<TContext> strategy) =>
        MaskWhole(JsonObserverItem<TContext>.ApplyStringPolicy(strategy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadStr"/>
    public JsonArrayBuilder<TContext> ReadStr(Action<string?, TContext> strategy)
        => MaskValue(JsonObserverItem<TContext>.ReadStr(strategy, _builderDefaultValuePolicy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskInt"/>
    public JsonArrayBuilder<TContext> MaskInt(Func<int?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyIntPolicy(strategy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadInt"/>
    public JsonArrayBuilder<TContext> ReadInt(Action<int?, TContext> strategy)
        => MaskValue(JsonObserverItem<TContext>.ReadInt(strategy, _builderDefaultValuePolicy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskLong"/>
    public JsonArrayBuilder<TContext> MaskLong(Func<long?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyLongPolicy(strategy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadLong"/>
    public JsonArrayBuilder<TContext> ReadLong(Action<long?, TContext> strategy)
        => MaskValue(JsonObserverItem<TContext>.ReadLong(strategy, _builderDefaultValuePolicy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskDecimal"/>
    public JsonArrayBuilder<TContext> MaskDecimal(Func<decimal?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyDecimalPolicy(strategy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadDecimal"/>
    public JsonArrayBuilder<TContext> ReadDecimal(Action<decimal?, TContext> strategy)
        => MaskValue(JsonObserverItem<TContext>.ReadDecimal(strategy, _builderDefaultValuePolicy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskBool"/>
    public JsonArrayBuilder<TContext> MaskBool(Func<bool?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyBoolPolicy(strategy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadBool"/>
    public JsonArrayBuilder<TContext> ReadBool(Action<bool?, TContext> strategy)
        => MaskValue(JsonObserverItem<TContext>.ReadBool(strategy, _builderDefaultValuePolicy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(Func{string, TContext, string})"/>
    public JsonArrayBuilder<TContext> MaskAny(Func<string?, TContext, string?> strategy)
        => MaskAny((StringMaskingStrategy<TContext>)strategy);

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(StringMaskingStrategy{TContext})"/>
    public JsonArrayBuilder<TContext> MaskAny(StringMaskingStrategy<TContext> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyAnyPolicy(strategy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskAny(MaskTag)"/>
    public JsonArrayBuilder<TContext> MaskAny(MaskTag tag)
        => MaskWhole(JsonObserverItem<TContext>.ApplyTagPolicy(tag));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskRawValue"/>
    public JsonArrayBuilder<TContext> MaskRawValue(Func<string?, TContext, string?> strategy)
        => MaskWhole(JsonObserverItem<TContext>.ApplyRawPolicy(strategy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.ReadRaw"/>
    public JsonArrayBuilder<TContext> ReadRaw(Action<string?, TContext> strategy)
        => MaskValue(JsonObserverItem<TContext>.ReadRaw(strategy, _builderDefaultValuePolicy));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskValue(JsonObserverValueDelegate{TContext})"/>
    public JsonArrayBuilder<TContext> MaskValue(JsonObserverValueDelegate<TContext> policy) =>
        MaskValue(
            (ref Utf8JsonReader reader, JsonWriter writer, TContext context, int depth, ref PropertyPath propPath, JsonObserverValueDelegate<TContext> _) =>
                policy(ref reader, writer, context, ref propPath));

    /// <inheritdoc cref="JsonValuePolicyBuilder{TContext}.PropertyMaskingStrategyBuilder.MaskValue(JsonObserverDelegate{TContext})"/>
    public JsonArrayBuilder<TContext> MaskValue(JsonObserverDelegate<TContext> policy)
        => Add(type => type.IsValueToken(), policy);

    /// <summary>
    /// Writes the string, number, boolean and <c>null</c> items unchanged; object and array items get the next rule or the default policy.
    /// </summary>
    public JsonArrayBuilder<TContext> Unmasked() => MaskValue(JsonObserverValuePolicies<TContext>.BlockList);

    internal JsonArrayBuilder<TContext> MaskWhole(JsonObserverDelegate<TContext> policy) => Add(_ => true, policy);

    internal static JsonObserverDelegate<TContext> Build(JsonArrayBuilder<TContext> builder) => builder.Build();

    private JsonArrayBuilder<TContext> Array(JsonObserverDelegate<TContext> policy) => Add(type => type == StartArray, policy);

    private JsonArrayBuilder<TContext> Obj(JsonObserverDelegate<TContext> policy) => Add(type => type == StartObject, policy);

    private JsonObserverDelegate<TContext> Build() => JsonObserverItem<TContext>.ApplyArrayPolicy([.. _policies], _builderDefaultValuePolicy);

    private JsonArrayBuilder<TContext> Add(Func<JsonTokenType, bool> typeMatch, JsonObserverDelegate<TContext> policy)
    {
        _policies.Add(new JsonObserverItem<TContext>((int depth, ref PropertyPath _, JsonTokenType type) => (typeMatch(type), depth + 1), policy));
        return this;
    }
}
