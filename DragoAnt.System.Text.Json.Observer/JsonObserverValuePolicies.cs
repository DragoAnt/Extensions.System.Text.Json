using DragoAnt.System.Text.Json.Observer.Builders;
using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Default policies for values no rule matches, for observers without a context. Pass one as the default policy of a factory or a rule.
/// </summary>
public static class JsonObserverValuePolicies
{
    /// <summary>
    /// The default policy, <see cref="AllowList"/>.
    /// </summary>
    public static readonly JsonObserverValueDelegate<JsonObserveringEmptyContext> Default
        = JsonObserverValuePolicies<JsonObserveringEmptyContext>.Default;

    /// <summary>
    /// Writes every value unchanged: only values matched by a rule are masked.
    /// </summary>
    public static readonly JsonObserverValueDelegate<JsonObserveringEmptyContext> BlockList
        = JsonObserverValuePolicies<JsonObserveringEmptyContext>.BlockList;

    /// <summary>
    /// Writes every string, number and boolean as <c>"***"</c> and keeps <c>null</c>: only values allowed by a rule are shown.
    /// </summary>
    public static readonly JsonObserverValueDelegate<JsonObserveringEmptyContext> AllowList
        = JsonObserverValuePolicies<JsonObserveringEmptyContext>.AllowList;

    /// <summary>
    /// The 1.x allow list: strings become <c>"#str#*****"</c>, numbers <c>"#number#*****"</c>, booleans and <c>null</c> pass.
    /// </summary>
    [Obsolete(LegacyAllowListMessage)]
    public static readonly JsonObserverValueDelegate<JsonObserveringEmptyContext> LegacyAllowList
        = JsonObserverValuePolicies<JsonObserveringEmptyContext>.LegacyAllowList;

    internal const string LegacyAllowListMessage =
        "The 1.x allow list keeps booleans and the type of masked values. Use AllowList, or JsonObserver.FromShape for a structure-aware allow list.";

    /// <summary>
    /// Writes every value as <c>null</c>.
    /// </summary>
    public static readonly JsonObserverValueDelegate<JsonObserveringEmptyContext> NullList
        = JsonObserverValuePolicies<JsonObserveringEmptyContext>.NullList;

    /// <summary>
    /// A default policy with its own rules that match the end of a property's path at any depth, for example every
    /// <c>password</c> or every <c>card.number</c>, wherever it is nested.
    /// </summary>
    /// <param name="init">Adds the rules, see <see cref="JsonValuePolicyBuilder{TContext}"/>.</param>
    /// <param name="defaultValuePolicy">Policy for values none of these rules match; <see cref="AllowList"/> when <c>null</c>.</param>
    /// <returns>A policy to pass where a default policy is expected.</returns>
    public static JsonObserverValueDelegate<JsonObserveringEmptyContext> Relative(
        Action<JsonValuePolicyBuilder<JsonObserveringEmptyContext>> init,
        JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultValuePolicy = null)
        => JsonObserverValuePolicies<JsonObserveringEmptyContext>.Relative(init, defaultValuePolicy);
}

/// <summary>
/// Default policies for values no rule matches. Pass one as the default policy of a factory or a rule.
/// </summary>
public static class JsonObserverValuePolicies<TContext>
{
    /// <summary>
    /// The default policy, <see cref="AllowList"/>.
    /// </summary>
    public static readonly JsonObserverValueDelegate<TContext> Default = AllowList;

    /// <summary>
    /// A default policy with its own rules that match the end of a property's path at any depth, for example every
    /// <c>password</c> or every <c>card.number</c>, wherever it is nested.
    /// </summary>
    /// <param name="init">Adds the rules, see <see cref="JsonValuePolicyBuilder{TContext}"/>.</param>
    /// <param name="defaultValuePolicy">Policy for values none of these rules match; <see cref="AllowList"/> when <c>null</c>.</param>
    /// <returns>A policy to pass where a default policy is expected.</returns>
    public static JsonObserverValueDelegate<TContext> Relative(
        Action<JsonValuePolicyBuilder<TContext>> init,
        JsonObserverValueDelegate<TContext>? defaultValuePolicy = null)
    {
        var builder = new JsonValuePolicyBuilder<TContext>(true, defaultValuePolicy);
        init(builder);
        var relative = new RelativeValuePolicy<TContext>(
            JsonValuePolicyBuilder<TContext>.Build(builder),
            JsonValuePolicyBuilder<TContext>.BuildItems(builder),
            defaultValuePolicy ?? Default);

        return relative.Invoke;
    }

    /// <summary>
    /// Writes every value as <c>null</c>.
    /// </summary>
    public static void NullList(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref PropertyPath propPath)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            case Number:
            case True:
            case False:
            case Null:
                writer.WriteNullValue();
                break;
            case Comment:
            case StartArray:
            case StartObject:
            case PropertyName:
            case EndObject:
            case EndArray:
            case JsonTokenType.None:
            default:
                throw new JsonObserverException("Wrong path");
        }
    }

    /// <summary>
    /// Writes every value unchanged: only values matched by a rule are masked.
    /// </summary>
    public static void BlockList(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref PropertyPath propPath)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                writer.CopyStringValue(ref reader);
                break;
            case Number:
                writer.CopyRawValue(ref reader);
                break;
            case True:
                writer.WriteBooleanValue(true);
                break;
            case False:
                writer.WriteBooleanValue(false);
                break;
            case Null:
                writer.WriteNullValue();
                break;
            case Comment:
            case StartArray:
            case StartObject:
            case PropertyName:
            case EndObject:
            case EndArray:
            case JsonTokenType.None:
            default:
                throw new JsonObserverException("Wrong path");
        }
    }

    /// <summary>
    /// Allow (or white) list policy approach: every value no rule allows is written as <c>"***"</c>, whatever its type;
    /// <c>null</c> stays <c>null</c>.
    /// </summary>
    public static void AllowList(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref PropertyPath propPath)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            case Number:
            case True:
            case False:
                writer.WriteStringValue("***"u8);
                break;
            case Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonObserverException("Wrong path");
        }
    }

    /// <summary>
    /// The 1.x allow list: strings become <c>"#str#*****"</c>, numbers <c>"#number#*****"</c>, booleans and <c>null</c> pass.
    /// </summary>
    [Obsolete(JsonObserverValuePolicies.LegacyAllowListMessage)]
    public static void LegacyAllowList(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref PropertyPath propPath)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                writer.WriteStringValue("#str#*****");
                break;
            case Number:
                writer.WriteStringValue("#number#*****");
                break;
            case True:
                writer.WriteBooleanValue(true);
                break;
            case False:
                writer.WriteBooleanValue(false);
                break;
            case Null:
                writer.WriteNullValue();
                break;
            case Comment:
            case StartArray:
            case StartObject:
            case PropertyName:
            case EndObject:
            case EndArray:
            case JsonTokenType.None:
            default:
                throw new JsonObserverException("Wrong path");
        }
    }
}

