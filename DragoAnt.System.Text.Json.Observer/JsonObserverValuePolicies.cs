using DragoAnt.System.Text.Json.Observer.Builders;
using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Value masking policies.
/// </summary>
public static class JsonObserverValuePolicies
{
    public static readonly JsonObserverValueDelegate<JsonObserveringEmptyContext> Default
        = JsonObserverValuePolicies<JsonObserveringEmptyContext>.Default;

    public static readonly JsonObserverValueDelegate<JsonObserveringEmptyContext> BlockList
        = JsonObserverValuePolicies<JsonObserveringEmptyContext>.BlockList;

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

    public static readonly JsonObserverValueDelegate<JsonObserveringEmptyContext> NullList
        = JsonObserverValuePolicies<JsonObserveringEmptyContext>.NullList;

    /// <summary>
    /// Initialize relative policy.
    /// </summary>
    /// <param name="init">Policy building action.</param>
    /// <param name="defaultValuePolicy">Default policy.</param>
    /// <remarks>
    /// Initializes policies for any property with or without considering depth.
    /// </remarks>
    public static JsonObserverValueDelegate<JsonObserveringEmptyContext> Relative(
        Action<JsonValuePolicyBuilder<JsonObserveringEmptyContext>> init,
        JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultValuePolicy = null)
        => JsonObserverValuePolicies<JsonObserveringEmptyContext>.Relative(init, defaultValuePolicy);
}

/// <summary>
/// Value masking policies.
/// </summary>
public static class JsonObserverValuePolicies<TContext>
{
    public static readonly JsonObserverValueDelegate<TContext> Default = AllowList;

    /// <summary>
    /// Initialize relative property value masking policy.
    /// </summary>
    /// <param name="init">Policy building action.</param>
    /// <param name="defaultValuePolicy">Default policy.</param>
    /// <remarks>
    /// Initializes policies for any property with or without considering depth.
    /// </remarks>
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
    /// Null list policy approach. All not specified properties will be null
    /// </summary>
    /// <remarks>
    /// Masks properties from specified list otherwise set null.
    /// </remarks>
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
    /// Block (or black) list policy approach.
    /// </summary>
    /// <remarks>
    /// Masks properties from specified list.
    /// </remarks>
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

