using System.Runtime.CompilerServices;
using System.Text;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static System.Text.Json.JsonTokenType;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Masks a payload against a <see cref="JsonShape"/>: known values are written as is, everything else is masked.
/// </summary>
internal sealed class ShapeWalker : PathExplainer
{
    private readonly JsonShape _root;
    private readonly JsonShape _unknown;
    private readonly bool _keepNulls;
    private readonly bool? _ignoreCase;

    public ShapeWalker(JsonShape root, JsonShapeOptions options)
    {
        root.Freeze();
        _root = root;
        _keepNulls = options.KeepNulls;
        _ignoreCase = options.PropertyNameCaseInsensitive;
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
                    var child = values ?? shape!.Find(name, _ignoreCase ?? propPath.PropertyNameCaseInsensitive) ?? _unknown;
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
        var index = 0;
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
                    propPath.AddArrayItem(index++);
                    Write(ref reader, writer, ref propPath, item);
                    propPath.RemovePropertyName();
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

    protected override (JsonPathOutcome Outcome, string Rule, string Action) Explain(
        IReadOnlyList<PathSegment> segments,
        JsonTokenType valueKind,
        bool propertyNameCaseInsensitive,
        List<string> steps)
    {
        var ignoreCase = _ignoreCase ?? propertyNameCaseInsensitive;
        var current = _root;
        var unknownMember = false;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var at = Format(segments, i + 1);
            var container = segment.IsIndex ? JsonShapeKind.Array : JsonShapeKind.Object;
            if (ReferenceEquals(current, JsonShape.UnknownPassThrough))
            {
                steps.Add($"{at}: inside an unknown member (PassThrough)");
                continue;
            }

            if (ReferenceEquals(current, JsonShape.UnknownDescend))
            {
                steps.Add($"{at}: inside an unknown member (Descend)");
                continue;
            }

            switch (current.Kind)
            {
                case JsonShapeKind.Object when !segment.IsIndex:
                {
                    var known = current.FindMember(Encoding.UTF8.GetBytes(segment.Name!), ignoreCase);
                    steps.Add(known is null ? $"{at}: unknown member ({Unknown})" : $"{at}: known member {known.Name} ({known.Shape.Kind})");
                    current = known?.Shape ?? _unknown;
                    unknownMember = known is null;
                    continue;
                }
                case JsonShapeKind.Map when !segment.IsIndex:
                    steps.Add($"{at}: dictionary value ({current.Item!.Kind})");
                    current = current.Item!;
                    continue;
                case JsonShapeKind.Array when segment.IsIndex:
                    steps.Add($"{at}: array item ({current.Item!.Kind})");
                    current = current.Item!;
                    continue;
                case JsonShapeKind.Masked:
                    return Masked(steps, at, $"shape Masked({current.Tag.Kind})", $"MaskTag.{current.Tag.Kind} on the whole value");
                default:
                    var expected = container == JsonShapeKind.Array ? "array" : "object";
                    return Masked(steps, at, $"shape {current.Kind} where the path has an {expected}", "writes \"***\" for the whole value");
            }
        }

        return Final(current, valueKind, steps, Format(segments), unknownMember);
    }

    private string Unknown => ReferenceEquals(_unknown, JsonShape.UnknownDescend) ? "Descend"
        : ReferenceEquals(_unknown, JsonShape.UnknownPassThrough) ? "PassThrough"
        : "MaskWhole";

    private (JsonPathOutcome, string, string) Final(JsonShape shape, JsonTokenType token, List<string> steps, string at, bool unknownMember)
    {
        var isContainer = token is StartObject or StartArray;
        var (outcome, rule, action) = shape switch
        {
            _ when ReferenceEquals(shape, JsonShape.UnknownPassThrough) => (JsonPathOutcome.Unchanged, "unknown member (PassThrough)", "writes the value as is"),
            _ when ReferenceEquals(shape, JsonShape.UnknownDescend) && isContainer => (JsonPathOutcome.Unchanged, "unknown member (Descend)", "shows the names, masks every value inside"),
            _ when ReferenceEquals(shape, JsonShape.UnknownDescend) => KeepNull(token, "unknown member (Descend)", "writes \"***\""),
            { Kind: JsonShapeKind.Scalar } when !isContainer => (JsonPathOutcome.Unchanged, "shape Scalar", "writes the value as is"),
            { Kind: JsonShapeKind.Masked } => KeepNull(token, $"shape Masked({shape.Tag.Kind})", $"MaskTag.{shape.Tag.Kind}"),
            { Kind: JsonShapeKind.Object or JsonShapeKind.Map } when token is StartObject => (JsonPathOutcome.Unchanged, $"shape {shape.Kind}", "applies the shape to the members"),
            { Kind: JsonShapeKind.Array } when token is StartArray => (JsonPathOutcome.Unchanged, "shape Array", "applies the item shape to every item"),
            { Kind: JsonShapeKind.Object or JsonShapeKind.Map or JsonShapeKind.Array } when token is Null => (JsonPathOutcome.Unchanged, $"shape {shape.Kind}", "keeps null"),
            _ when unknownMember => KeepNull(token, "unknown member (MaskWhole)", "writes \"***\" for the whole value"),
            { Kind: JsonShapeKind.Opaque } => KeepNull(token, "shape Opaque", "writes \"***\" for the whole value"),
            _ => KeepNull(token, $"shape {shape.Kind} does not fit a {token} value", "writes \"***\" for the whole value"),
        };

        steps.Add($"{at}: {rule} → {action}");
        return (outcome, rule, action);
    }

    private (JsonPathOutcome, string, string) KeepNull(JsonTokenType token, string rule, string action) =>
        token is Null && _keepNulls ? (JsonPathOutcome.Unchanged, rule, "keeps null") : (JsonPathOutcome.Masked, rule, action);

    private static (JsonPathOutcome, string, string) Masked(List<string> steps, string at, string rule, string action)
    {
        steps.Add($"{at}: {rule} → {action}");
        return (JsonPathOutcome.Masked, rule, action);
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
