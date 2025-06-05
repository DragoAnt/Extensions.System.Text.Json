using DragoAnt.System.Text.Json.Observer.Builders;
using static System.Text.Json.JsonTokenType;
using static DragoAnt.System.Text.Json.Observer.JsonWriter.IgnoreNullsJsonTokenType;

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
        var policy = JsonValuePolicyBuilder<TContext>.Build(builder);
        defaultValuePolicy ??= Default;

        return (ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref PropertyPath propPath)
            => policy(ref reader, writer, context, 0, ref propPath, defaultValuePolicy);
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
                writer.WriteStringValue(reader.GetString());
                break;
            case Number:
                if (reader.TryGetInt64(out var int64))
                {
                    writer.WriteNumberValue(int64);
                }
                else
                {
                    writer.WriteNumberValue(reader.GetDecimal());
                }

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
    /// Allow (or white) list policy approach.
    /// </summary>
    /// <remarks>
    /// Masks all properties by default, props from the list will not mask.
    /// </remarks>
    public static void AllowList(ref Utf8JsonReader reader, JsonWriter writer, TContext context, ref PropertyPath propPath)
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

public abstract class JsonWriter
{
    public static readonly JsonWriter Empty = new EmptyJsonWriter();

    public static JsonWriter FromUtf8JsonWriter(Utf8JsonWriter writer, bool ignoreNulls = false, bool ignoreComments = false)
        => ignoreNulls
            ? new IgnoreNullsTextJsonWriter(writer, ignoreComments)
            : new TextJsonWriter(writer, ignoreComments);

    public abstract void WriteNullValue();
    public abstract void WriteBooleanValue(bool value);
    public abstract void WriteStringValue(string? value);
    public abstract void WriteNumberValue(long value);
    public abstract void WriteNumberValue(decimal value);
    public abstract void WriteCommentValue(string comment);
    public abstract void WritePropertyName(string propertyName);
    public abstract void WriteStartObject();
    public abstract void WriteEndObject();
    public abstract void WriteStartArray();
    public abstract void WriteEndArray();

    private sealed class EmptyJsonWriter : JsonWriter
    {
        public override void WriteNullValue()
        {
        }

        public override void WriteBooleanValue(bool value)
        {
        }

        public override void WriteStringValue(string? value)
        {
        }

        public override void WriteNumberValue(long value)
        {
        }

        public override void WriteNumberValue(decimal value)
        {
        }

        public override void WriteCommentValue(string comment)
        {
        }

        public override void WritePropertyName(string propertyName)
        {
        }

        public override void WriteStartObject()
        {
        }

        public override void WriteEndObject()
        {
        }

        public override void WriteStartArray()
        {
        }

        public override void WriteEndArray()
        {
        }
    }

    private sealed class TextJsonWriter(Utf8JsonWriter writer, bool ignoreComments) : JsonWriter
    {
        public override void WriteNullValue() => writer.WriteNullValue();
        public override void WriteBooleanValue(bool value) => writer.WriteBooleanValue(value);
        public override void WriteStringValue(string? value) => writer.WriteStringValue(value);
        public override void WriteNumberValue(long value) => writer.WriteNumberValue(value);
        public override void WriteNumberValue(decimal value) => writer.WriteNumberValue(value);

        public override void WriteCommentValue(string comment)
        {
            if (ignoreComments)
            {
                return;
            }

            writer.WriteCommentValue(comment);
        }

        public override void WritePropertyName(string propertyName) => writer.WritePropertyName(propertyName);
        public override void WriteStartObject() => writer.WriteStartObject();
        public override void WriteEndObject() => writer.WriteEndObject();
        public override void WriteStartArray() => writer.WriteStartArray();
        public override void WriteEndArray() => writer.WriteEndArray();
    }

    private sealed class IgnoreNullsTextJsonWriter(Utf8JsonWriter writer, bool ignoreComments = false) : JsonWriter
    {
        private readonly Stack<(IgnoreNullsJsonTokenType type, string? propName)> _stack = new();

        public override void WriteNullValue()
        {
            var item = _stack.Peek();
            switch (item.type)
            {
                case StartArr:
                case StartedArr:
                    return;
                case PropName:
                    _stack.Pop();
                    return;
                default: throw new InvalidOperationException("Unexpected type");
            }
        }

        public override void WriteBooleanValue(bool value)
        {
            StartGroupOnStack();
            writer.WriteBooleanValue(value);
        }


        public override void WriteStringValue(string? value)
        {
            StartGroupOnStack();
            writer.WriteStringValue(value);
        }

        public override void WriteNumberValue(long value)
        {
            StartGroupOnStack();
            writer.WriteNumberValue(value);
        }

        public override void WriteNumberValue(decimal value)
        {
            StartGroupOnStack();
            writer.WriteNumberValue(value);
        }

        public override void WriteCommentValue(string comment)
        {
            if (ignoreComments)
            {
                return;
            }

            StartGroupOnStack();
            writer.WriteCommentValue(comment);
        }

        public override void WritePropertyName(string propertyName) => _stack.Push((PropName, propertyName));
        public override void WriteStartObject() => _stack.Push((StartObj, null));

        public override void WriteEndObject()
        {
            var item = _stack.Pop();
            switch (item.type)
            {
                case StartObj:
                    if (_stack.TryPeek(out item) && item.type is PropName)
                    {
                        _stack.Pop();
                    }
                    return;
                case StartedObj:
                    StartGroupOnStack();
                    writer.WriteEndObject();
                    break;
                default: throw new InvalidOperationException("Unexpected type");
            }
        }

        public override void WriteStartArray() => _stack.Push((StartArr, null));

        public override void WriteEndArray()
        {
            var item = _stack.Pop();
            switch (item.type)
            {
                case StartArr:
                    if (_stack.TryPeek(out item) && item.type is PropName)
                    {
                        _stack.Pop();
                    }
                    return;
                case StartedArr:
                    StartGroupOnStack();
                    writer.WriteEndArray();
                    break;
                default: throw new InvalidOperationException("Unexpected type");
            }
        }

        private void StartGroupOnStack()
        {
            if (!_stack.TryPeek(out var item))
            {
                return;
            }

            switch (item.type)
            {
                case StartedObj:
                case StartedArr:
                    break;
                case StartObj:
                    _stack.Pop();
                    StartGroupOnStack();
                    writer.WriteStartObject();
                    _stack.Push((StartedObj, null));
                    return;
                case StartArr:
                    _stack.Pop();
                    StartGroupOnStack();
                    writer.WriteStartArray();
                    _stack.Push((StartedArr, null));
                    return;
                case PropName:
                    item = _stack.Pop();
                    StartGroupOnStack();
                    writer.WritePropertyName(item.propName!);
                    return;
                default: throw new InvalidOperationException("Unexpected type");
            }
        }
    }

    public enum IgnoreNullsJsonTokenType : byte
    {
        None,
        StartObj,
        StartedObj,
        StartArr,
        StartedArr,
        PropName,
    }
}