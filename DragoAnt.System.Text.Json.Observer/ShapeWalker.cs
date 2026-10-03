using System.Runtime.CompilerServices;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Masks a payload against a <see cref="JsonShape"/>: known values are written as is, everything else is masked.
/// </summary>
internal sealed class ShapeWalker
{
    private readonly JsonShape _root;
    private readonly JsonShape _unknown;
    private readonly bool _keepNulls;

    public ShapeWalker(JsonShape root, JsonShapeOptions options)
    {
        root.Seal([]);
        _root = root;
        _keepNulls = options.KeepNulls;
        _unknown = options.Unknown switch
        {
            UnknownMemberPolicy.Descend => JsonShape.UnknownDescend,
            UnknownMemberPolicy.PassThrough => JsonShape.UnknownPassThrough,
            _ => JsonShape.Opaque,
        };
    }

    public void Invoke(
        ref Utf8JsonReader reader,
        JsonWriter writer,
        JsonObserveringEmptyContext context,
        int depth,
        ref PropertyPath propPath,
        JsonObserverValueDelegate<JsonObserveringEmptyContext> defaultValue)
        => Write(ref reader, writer, ref propPath, _root);

    private void Write(ref Utf8JsonReader reader, JsonWriter writer, ref PropertyPath propPath, JsonShape shape)
    {
        var token = reader.TokenType;
        if (ReferenceEquals(shape, JsonShape.UnknownPassThrough))
        {
            Copy(ref reader, writer, ref propPath, shape, maskScalars: false);
            return;
        }

        if (ReferenceEquals(shape, JsonShape.UnknownDescend))
        {
            Copy(ref reader, writer, ref propPath, shape, maskScalars: true);
            return;
        }

        switch (shape.Kind)
        {
            case JsonShapeKind.Scalar when token is not (StartObject or StartArray):
                CopyScalar(ref reader, writer);
                return;
            case JsonShapeKind.Masked:
                MaskWhole(ref reader, writer, ref propPath, shape.Tag);
                return;
            case JsonShapeKind.Object or JsonShapeKind.Map or JsonShapeKind.Array when token is Null:
                writer.WriteNullValue();
                return;
            case JsonShapeKind.Object when token is StartObject:
                WriteObject(ref reader, writer, ref propPath, shape, null);
                return;
            case JsonShapeKind.Map when token is StartObject:
                WriteObject(ref reader, writer, ref propPath, null, shape.Item!);
                return;
            case JsonShapeKind.Array when token is StartArray:
                WriteArray(ref reader, writer, ref propPath, shape.Item!);
                return;
            default:
                MaskWhole(ref reader, writer, ref propPath, MaskTag.Full);
                return;
        }
    }

    private void WriteObject(ref Utf8JsonReader reader, JsonWriter writer, ref PropertyPath propPath, JsonShape? shape, JsonShape? values)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        writer.WriteStartObject();
        while (true)
        {
            if (propPath.Stopped || writer.Stopped || !reader.Read())
            {
                propPath.Stop();
                return;
            }

            switch (reader.TokenType)
            {
                case EndObject:
                    writer.WriteEndObject();
                    return;
                case PropertyName:
                    propPath.AddPropertyName(ref reader);
                    var name = propPath.CurrentUtf8;
                    var child = values ?? shape!.Find(name) ?? _unknown;
                    if (!reader.Read())
                    {
                        propPath.RemovePropertyName();
                        propPath.Stop();
                        return;
                    }

                    writer.WritePropertyName(name);
                    Write(ref reader, writer, ref propPath, child);
                    propPath.RemovePropertyName();
                    break;
            }
        }
    }

    private void WriteArray(ref Utf8JsonReader reader, JsonWriter writer, ref PropertyPath propPath, JsonShape item)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        writer.WriteStartArray();
        while (true)
        {
            if (propPath.Stopped || writer.Stopped || !reader.Read())
            {
                propPath.Stop();
                return;
            }

            switch (reader.TokenType)
            {
                case EndArray:
                    writer.WriteEndArray();
                    return;
                case Comment:
                    break;
                default:
                    Write(ref reader, writer, ref propPath, item);
                    break;
            }
        }
    }

    /// <summary>
    /// Writes an unknown value: containers are descended with the same treatment, scalars are masked or copied.
    /// </summary>
    private void Copy(ref Utf8JsonReader reader, JsonWriter writer, ref PropertyPath propPath, JsonShape mode, bool maskScalars)
    {
        switch (reader.TokenType)
        {
            case StartObject:
                WriteObject(ref reader, writer, ref propPath, null, mode);
                return;
            case StartArray:
                WriteArray(ref reader, writer, ref propPath, mode);
                return;
            default:
                if (maskScalars)
                {
                    MaskWhole(ref reader, writer, ref propPath, MaskTag.Full);
                }
                else
                {
                    CopyScalar(ref reader, writer);
                }

                return;
        }
    }

    private void MaskWhole(ref Utf8JsonReader reader, JsonWriter writer, ref PropertyPath propPath, MaskTag tag)
    {
        if (reader.TokenType is Null && _keepNulls)
        {
            writer.WriteNullValue();
            return;
        }

        TagMasking.Mask(ref reader, writer, tag, ref propPath);
    }

    private static void CopyScalar(ref Utf8JsonReader reader, JsonWriter writer)
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
            case False:
                writer.WriteBooleanValue(reader.TokenType is True);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
