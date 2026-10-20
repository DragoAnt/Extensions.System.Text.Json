using System.Buffers;
using DragoAnt.Observer;
using System.Globalization;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

internal static class MatrixObservers
{
    internal static IReadOnlyDictionary<string, MatrixObserver> All { get; } = Create();

    private static IReadOnlyDictionary<string, MatrixObserver> Create()
    {
        static JsonObserver Rules(Action<Builders.JsonAnyDepthBuilder<NoContext>>? extra = null)
        {
            return JsonObserver.Any(
                _ => { },
                _ => { },
                AnyDepth(b =>
                {
                    b.Match(Names.OneOf("password", "pin", "driverLicense", "ssn", "token", "blob")).Mask(MaskTag.Full)
                        .Match("card").Mask(MaskTag.Last4);
                    extra?.Invoke(b);
                }, BlockList));
        }

        var allow = JsonObserver.Any(
            o => o.Match(Names.OneOf("id", "name", "note", "naïve", "n", "ok", "z")).Unmasked(),
            _ => { });

        var node = JsonShape.Object(
            ("id", JsonShape.Scalar), ("name", JsonShape.Scalar), ("note", JsonShape.Scalar), ("naïve", JsonShape.Scalar),
            ("n", JsonShape.Scalar), ("ok", JsonShape.Scalar), ("z", JsonShape.Scalar),
            ("password", JsonShape.Masked(MaskTag.Full)), ("pin", JsonShape.Masked(MaskTag.Full)),
            ("driverLicense", JsonShape.Masked(MaskTag.Full)), ("ssn", JsonShape.Masked(MaskTag.Full)),
            ("token", JsonShape.Masked(MaskTag.Full)), ("card", JsonShape.Masked(MaskTag.Last4)),
            ("blob", JsonShape.Masked(MaskTag.Full)));
        node.Add("nested", node).Add("items", JsonShape.Array(node)).Add("list", JsonShape.Array(node));

        return new Dictionary<string, MatrixObserver>
        {
            ["anydepth-blocklist"] = new(_ => new(Rules())),
            ["allowlist-unmasked"] = new(_ => new(allow)),
            ["shape-maskwhole"] = new(array => new(JsonObserver.FromShape(array ? JsonShape.Array(node) : node))),
            ["shape-descend"] = new(array => new(JsonObserver.FromShape(array ? JsonShape.Array(node) : node, new JsonShapeOptions { Unknown = UnknownMemberPolicy.Descend }))),
            ["shape-passthrough"] = new(array => new(JsonObserver.FromShape(array ? JsonShape.Array(node) : node, new JsonShapeOptions { Unknown = UnknownMemberPolicy.PassThrough }))),
            ["extraction"] = new(_ => new(CreateExtraction())),
            ["comment-keep"] = new(_ => new(Rules(b => b.Match("note").Unmasked().Comment(CommentKind.Any, CommentRules.Keep)))),
            ["comment-drop"] = new(_ => new(Rules(b => b.Match("note").Unmasked().Comment(CommentKind.Any, CommentRules.Drop)))),
            ["comment-masked-owner"] = new(_ => new(Rules(b => b.Match("password").Mask(MaskTag.Full).Comment(CommentKind.Any, CommentRules.Keep)))),
        };
    }

    internal static JsonObserver<MatrixExtracted> CreateExtraction() =>
        JsonObserver.Any<MatrixExtracted>(
            _ => { },
            _ => { },
            AnyDepth<MatrixExtracted>(b => b
                .Match("id").ReadInt((value, context) => { if (value.HasValue) context.Values.Add(new("integer", value.Value.ToString(CultureInfo.InvariantCulture))); }).Unmasked()
                .Match(Names.OneOf("name", "note")).ReadStr((value, context) => { if (value is not null) context.Values.Add(new("string", value)); }).Unmasked()
                .Match(Names.OneOf("password", "pin", "driverLicense", "ssn", "token", "blob"))
                    .ReadStr((value, context) => { if (value is not null) context.Values.Add(new("string", value)); }).Mask(MaskTag.Full)
                .Match("card").ReadStr((value, context) => { if (value is not null) context.Values.Add(new("string", value)); }).Mask(MaskTag.Last4)
                .Match("n").ReadDecimal((value, context) => { if (value.HasValue) context.Values.Add(new("decimal", value.Value.ToString(CultureInfo.InvariantCulture))); }).Unmasked()
                .Match("ok").ReadBool((value, context) => { if (value.HasValue) context.Values.Add(new("boolean", value.Value.ToString().ToLowerInvariant())); }).Unmasked(),
                BlockList));
}

internal sealed record MatrixObserver(Func<bool, MatrixObserverInstance> Create);

internal sealed class MatrixObserverInstance
{
    private readonly JsonObserver? _masking;
    private readonly JsonObserver<MatrixExtracted>? _extracting;

    internal MatrixObserverInstance(JsonObserver masking) => _masking = masking;

    internal MatrixObserverInstance(JsonObserver<MatrixExtracted> extracting) => _extracting = extracting;

    internal string? Mask(string json, JsonObserverOptions? options = null) =>
        _masking?.Mask(json, options) ?? _extracting!.Mask(json, new MatrixExtracted(), options);

    internal string? Mask(string json, out MaskResult result, JsonObserverOptions? options = null)
    {
        if (_masking is not null) return _masking.Mask(json, out result, options);
        return _extracting!.Mask(json, new MatrixExtracted(), out result, options);
    }

    internal MaskResult Mask(ReadOnlySpan<byte> input, IBufferWriter<byte> output, JsonObserverOptions? options = null) =>
        _masking?.Mask(input, output, options) ?? _extracting!.Mask(input, output, new MatrixExtracted(), options);

    internal MaskResult Mask(in ReadOnlySequence<byte> input, IBufferWriter<byte> output, JsonObserverOptions? options = null) =>
        _masking?.Mask(input, output, options) ?? _extracting!.Mask(input, output, new MatrixExtracted(), options);

    internal MaskResult Mask(ReadOnlySpan<char> input, IBufferWriter<byte> output, JsonObserverOptions? options = null) =>
        _masking?.Mask(input, output, options) ?? _extracting!.Mask(input, output, new MatrixExtracted(), options);

    internal MaskResult Mask(ReadOnlySpan<char> input, IBufferWriter<char> output, JsonObserverOptions? options = null) =>
        _masking?.Mask(input, output, options) ?? _extracting!.Mask(input, output, new MatrixExtracted(), options);
}

internal sealed record MatrixReadValue(string Kind, string Value);

internal sealed class MatrixExtracted
{
    internal List<MatrixReadValue> Values { get; } = [];
}
