using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using DragoAnt.System.Text.Json.Observer.Builders;

// ReSharper disable MemberCanBePrivate.Global

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Masks JSON in one streaming pass from <see cref="Utf8JsonReader"/> to <see cref="Utf8JsonWriter"/>, without deserializing it.
/// Build it once with a factory method and share it: it is thread-safe.
/// </summary>
public sealed class JsonObserver
{
    private readonly JsonObserver<NoContext> _masking;

    /// <summary>
    /// Creates an observer for a payload whose root is an object or an array.
    /// </summary>
    /// <param name="initObj">Adds the rules for a root object.</param>
    /// <param name="initArray">Adds the rules for a root array.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    public static JsonObserver Any(
        Action<JsonObjBuilder<NoContext>> initObj,
        Action<JsonArrayBuilder<NoContext>> initArray,
        JsonValuePolicy<NoContext>? defaultMasking = null) =>
        new(Any<NoContext>(initObj, initArray, defaultMasking));

    /// <summary>
    /// Creates an observer for a root object that applies one policy to every value, for example
    /// <see cref="JsonValuePolicy.AnyDepth(Action{Builders.JsonAnyDepthBuilder{NoContext}}, JsonValuePolicy{NoContext}?)"/>. A root array is <see cref="MaskStatus.Invalid"/>.
    /// </summary>
    /// <param name="defaultMasking">Policy for every value; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    public static JsonObserver Obj(JsonValuePolicy<NoContext>? defaultMasking) =>
        new(Obj<NoContext>(defaultMasking));

    /// <summary>
    /// Creates an observer for a root object. A root array is <see cref="MaskStatus.Invalid"/>; use <see cref="Any"/> for both.
    /// </summary>
    /// <param name="init">Adds the rules for the object's properties.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    public static JsonObserver Obj(
        Action<JsonObjBuilder<NoContext>> init,
        JsonValuePolicy<NoContext>? defaultMasking = null) =>
        new(Obj<NoContext>(init, defaultMasking));

    /// <summary>
    /// Creates an observer for a root array that applies one policy to every value. A root object is <see cref="MaskStatus.Invalid"/>.
    /// </summary>
    /// <param name="defaultMasking">Policy for every value; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    public static JsonObserver Array(JsonValuePolicy<NoContext>? defaultMasking) =>
        new(Array<NoContext>(defaultMasking));

    /// <summary>
    /// Creates an observer for a root array. A root object is <see cref="MaskStatus.Invalid"/>; use <see cref="Any"/> for both.
    /// </summary>
    /// <param name="init">Adds the rules for the array's items.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    public static JsonObserver Array(
        Action<JsonArrayBuilder<NoContext>> init,
        JsonValuePolicy<NoContext>? defaultMasking = null) =>
        new(Array<NoContext>(init, defaultMasking));

    /// <summary>
    /// Creates an observer that also extracts values into a <typeparamref name="TContext"/>, for a root object or array.
    /// </summary>
    /// <param name="initObj">Adds the rules for a root object.</param>
    /// <param name="initArray">Adds the rules for a root array.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    public static JsonObserver<TContext> Any<TContext>(
        Action<JsonObjBuilder<TContext>> initObj,
        Action<JsonArrayBuilder<TContext>> initArray,
        JsonValuePolicy<TContext>? defaultMasking = null)
    {
        var (masking, obj, array) = JsonObserverItem<TContext>.Any(initObj, initArray, defaultMasking);
        return new JsonObserver<TContext>(masking, new RuleExplainer<TContext>(obj, array), CommentRuleScan.Any(obj) || CommentRuleScan.Any(array));
    }

    /// <summary>
    /// Creates an observer with a context for a root object that applies one policy to every value.
    /// </summary>
    /// <param name="defaultMasking">Policy for every value; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    public static JsonObserver<TContext> Obj<TContext>(JsonValuePolicy<TContext>? defaultMasking) =>
        Obj<TContext>(_ => { }, defaultMasking);

    /// <summary>
    /// Creates an observer that also extracts values into a <typeparamref name="TContext"/>, for a root object.
    /// </summary>
    /// <param name="init">Adds the rules for the object's properties.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    public static JsonObserver<TContext> Obj<TContext>(
        Action<JsonObjBuilder<TContext>> init,
        JsonValuePolicy<TContext>? defaultMasking = null)
    {
        var (masking, set) = JsonObserverItem<TContext>.Obj(init, defaultMasking);
        return new JsonObserver<TContext>(masking, new RuleExplainer<TContext>(set, null), CommentRuleScan.Any(set));
    }

    /// <summary>
    /// Creates an observer with a context for a root array that applies one policy to every value.
    /// </summary>
    /// <param name="defaultMasking">Policy for every value; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    public static JsonObserver<TContext> Array<TContext>(JsonValuePolicy<TContext>? defaultMasking) =>
        Array<TContext>(_ => { }, defaultMasking);

    /// <summary>
    /// Creates an observer that also extracts values into a <typeparamref name="TContext"/>, for a root array.
    /// </summary>
    /// <param name="init">Adds the rules for the array's items.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="ValuePolicy.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    public static JsonObserver<TContext> Array<TContext>(
        Action<JsonArrayBuilder<TContext>> init,
        JsonValuePolicy<TContext>? defaultMasking = null)
    {
        var (masking, set) = JsonObserverItem<TContext>.Array(init, defaultMasking);
        return new JsonObserver<TContext>(masking, new RuleExplainer<TContext>(null, set), CommentRuleScan.Any(set));
    }

    /// <summary>
    /// Creates an observer that masks against an expected structure: values of known properties are written as is,
    /// sensitive ones are masked with their tag, and anything the shape does not describe is handled by
    /// <see cref="JsonShapeOptions.Unknown"/>.
    /// </summary>
    /// <param name="shape">Expected structure, for example from <see cref="JsonShape.FromTypeInfo"/>. It cannot change afterwards.</param>
    /// <param name="options">Treatment of unknown properties and of <c>null</c>.</param>
    public static JsonObserver FromShape(JsonShape shape, JsonShapeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var walker = new ShapeWalker(shape, options ?? JsonShapeOptions.Default);
        return new JsonObserver(new JsonObserver<NoContext>(walker.Invoke, walker, hasCommentRules: false));
    }

    private JsonObserver(JsonObserver<NoContext> masking)
    {
        _masking = masking;
    }

    /// <summary>
    /// Masks a JSON text. Never throws: invalid or cut-off JSON yields its safe masked prefix, and only masked values
    /// are ever written. The output is the same as <see cref="Mask(ReadOnlySpan{byte}, IBufferWriter{byte}, JsonObserverOptions?)"/>
    /// writes for the same text.
    /// </summary>
    /// <param name="json">JSON text; it may be cut short, for example by a size limit.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>The masked JSON; empty when the text is not a JSON object or array; <c>null</c> for <c>null</c>.</returns>
    public string? Mask(string? json, JsonObserverOptions? options = null)
        => _masking.Mask(json, NoContext.Instance, out _, options);

    /// <summary>
    /// Masks a JSON text and reports what happened. Never throws: invalid or cut-off JSON yields its safe masked prefix,
    /// and only masked values are ever written.
    /// </summary>
    /// <param name="json">JSON text; it may be cut short, for example by a size limit.</param>
    /// <param name="result">Status, UTF-8 length of the output and UTF-8 offset where reading stopped.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>The masked JSON; empty when the text is not a JSON object or array; <c>null</c> for <c>null</c>.</returns>
    public string? Mask(string? json, out MaskResult result, JsonObserverOptions? options = null)
        => _masking.Mask(json, NoContext.Instance, out result, options);

    /// <summary>
    /// Masks a UTF-8 JSON payload into <paramref name="output"/>. Never throws: problems are reported in the result,
    /// and only masked values are ever written.
    /// </summary>
    /// <param name="utf8">UTF-8 JSON payload; it may be cut short, for example by a size limit. A leading byte order mark is skipped.</param>
    /// <param name="output">Receives the masked JSON.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>Status, bytes written and the input offset where reading stopped.</returns>
    public MaskResult Mask(ReadOnlySpan<byte> utf8, IBufferWriter<byte> output, JsonObserverOptions? options = null)
        => _masking.Mask(utf8, output, NoContext.Instance, options);

    /// <summary>
    /// Masks a UTF-8 JSON payload held in several buffers, for example read from a <c>PipeReader</c>, into
    /// <paramref name="output"/> without copying it into one buffer first. Never throws, and writes exactly what
    /// <see cref="Mask(ReadOnlySpan{byte}, IBufferWriter{byte}, JsonObserverOptions?)"/> writes for the same bytes,
    /// however they are split.
    /// </summary>
    /// <param name="utf8">UTF-8 JSON payload; it may be cut short, for example by a size limit. A leading byte order mark is skipped.</param>
    /// <param name="output">Receives the masked JSON.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>Status, bytes written and the input offset where reading stopped.</returns>
    public MaskResult Mask(in ReadOnlySequence<byte> utf8, IBufferWriter<byte> output, JsonObserverOptions? options = null)
        => _masking.Mask(utf8, output, NoContext.Instance, options);

    /// <inheritdoc cref="JsonObserver{TContext}.Explain"/>
    public PathExplanation Explain([StringSyntax(PathSyntax)] string path, ValueKind valueKind = ValueKind.String, JsonObserverOptions? options = null)
        => _masking.Explain(path, valueKind, options);

    /// <summary>
    /// The syntax name of the paths <c>Explain</c> accepts, for editors that highlight string syntaxes.
    /// </summary>
    public const string PathSyntax = "DragoAnt.ObserverPath";
}

/// <summary>
/// Masks JSON and hands values to a context of type <typeparamref name="TContext"/> in one streaming pass.
/// Build it once and share it: it is thread-safe.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
public sealed class JsonObserver<TContext>
{
    private readonly ObserveRule<TContext> _maskDelegate;
    private readonly PathExplainer _explainer;
    private int _maxDepth = 6;

    private readonly bool _hasCommentRules;

    internal JsonObserver(ObserveRule<TContext> maskDelegate, PathExplainer explainer, bool hasCommentRules)
    {
        _hasCommentRules = hasCommentRules;
        _maskDelegate = maskDelegate;
        _explainer = explainer;
    }

    /// <summary>
    /// Tells which rule or policy handles the value at <paramref name="path"/> and what it does with it, without
    /// masking anything: useful to check a configuration, to document it, or to find out why a value was masked.
    /// </summary>
    /// <param name="path">
    /// A concrete JSON path such as <c>items[2].sku</c>, <c>$.order.card.number</c> or <c>$['a.b']</c> (no wildcards or
    /// recursive descent); the first segment decides whether the root is an object or an array.
    /// </param>
    /// <param name="valueKind">Type of the value at the path; rules can differ by type.</param>
    /// <param name="options">The call's options, for <see cref="ObserverOptions.NameCaseInsensitive"/>.</param>
    /// <returns>The deciding rule, its action, the outcome and the steps that lead there.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a concrete JSON path.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="valueKind"/> is not a defined value.</exception>
    public PathExplanation Explain([StringSyntax(JsonObserver.PathSyntax)] string path, ValueKind valueKind = ValueKind.String, JsonObserverOptions? options = null)
        => _explainer.Explain(path, valueKind, (options ?? JsonObserverOptions.Default).NameCaseInsensitive);

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Masks a JSON text and hands values to <paramref name="context"/>. Never throws: invalid or cut-off JSON yields
    /// its safe masked prefix, and only masked values are ever written.
    /// </summary>
    /// <param name="json">JSON text; it may be cut short, for example by a size limit.</param>
    /// <param name="context">Receives the values read rules extract.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>The masked JSON; empty when the text is not a JSON object or array; <c>null</c> for <c>null</c>.</returns>
    public string? Mask(string? json, TContext context, JsonObserverOptions? options = null)
        => Mask(json, context, out _, options);

    /// <summary>
    /// Masks a JSON text, hands values to <paramref name="context"/> and reports what happened. Never throws:
    /// invalid or cut-off JSON yields its safe masked prefix, and only masked values are ever written.
    /// </summary>
    /// <param name="json">JSON text; it may be cut short, for example by a size limit.</param>
    /// <param name="context">Receives the values read rules extract.</param>
    /// <param name="result">Status, UTF-8 length of the output and UTF-8 offset where reading stopped.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>The masked JSON; empty when the text is not a JSON object or array; <c>null</c> for <c>null</c>.</returns>
    public string? Mask(string? json, TContext context, out MaskResult result, JsonObserverOptions? options = null)
    {
        if (json is null)
        {
            result = new MaskResult { Status = MaskStatus.Unrecognized };
            return null;
        }

        byte[]? input = null;
        PooledBufferWriter? output = null;
        try
        {
            input = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(json));
            var utf8 = input.AsSpan(0, Encoding.UTF8.GetBytes(json, input));
            output = PooledBufferWriter.Rent(Math.Clamp(utf8.Length, 256, 64 * 1024));
            result = Mask(utf8, output, context, options);
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
        catch (Exception)
        {
            result = new MaskResult { Status = MaskStatus.Invalid };
            return string.Empty;
        }
        finally
        {
            output?.Return();
            if (input is not null)
            {
                ArrayPool<byte>.Shared.Return(input, clearArray: true);
            }
        }
    }

    /// <summary>
    /// Masks a UTF-8 JSON payload into <paramref name="output"/> and hands values to <paramref name="context"/>.
    /// Never throws: problems are reported in the result, and only masked values are ever written.
    /// </summary>
    /// <param name="utf8">UTF-8 JSON payload; it may be cut short, for example by a size limit. A leading byte order mark is skipped.</param>
    /// <param name="output">Receives the masked JSON.</param>
    /// <param name="context">Receives the values read rules extract.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>Status, bytes written and the input offset where reading stopped.</returns>
    public MaskResult Mask(ReadOnlySpan<byte> utf8, IBufferWriter<byte> output, TContext context, JsonObserverOptions? options = null)
    {
        options ??= JsonObserverOptions.Default;
        utf8 = SkipBom(utf8);
        var comments = CommentsOf(options);
        var reader = CreateReader(utf8, options, comments is not null);
        return Mask(ref reader, utf8, default, output, context, options, comments);
    }

    /// <summary>
    /// Masks a UTF-8 JSON payload held in several buffers, for example read from a <c>PipeReader</c>, into
    /// <paramref name="output"/> and hands values to <paramref name="context"/>, without copying it into one buffer first.
    /// Never throws, and behaves exactly like <see cref="Mask(ReadOnlySpan{byte}, IBufferWriter{byte}, TContext, JsonObserverOptions?)"/>
    /// for the same bytes, however they are split.
    /// </summary>
    /// <param name="utf8">UTF-8 JSON payload; it may be cut short, for example by a size limit. A leading byte order mark is skipped.</param>
    /// <param name="output">Receives the masked JSON.</param>
    /// <param name="context">Receives the values read rules extract.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>Status, bytes written and the input offset where reading stopped.</returns>
    public MaskResult Mask(in ReadOnlySequence<byte> utf8, IBufferWriter<byte> output, TContext context, JsonObserverOptions? options = null)
    {
        if (utf8.IsSingleSegment)
        {
            return Mask(utf8.FirstSpan, output, context, options);
        }

        options ??= JsonObserverOptions.Default;
        var comments = CommentsOf(options);
        var input = SkipBom(utf8);
        var reader = CreateReader(input, options, comments is not null);
        return Mask(ref reader, default, input, output, context, options, comments);
    }

    private MaskResult Mask(
        ref Utf8JsonReader reader,
        ReadOnlySpan<byte> input,
        in ReadOnlySequence<byte> sequence,
        IBufferWriter<byte> output,
        TContext context,
        JsonObserverOptions options,
        CommentPolicy? comments)
    {
        using var bounded = BoundedJsonWriter.Rent(options);
        using var ignoreNulls = options.IgnoreNulls ? IgnoreNullsJsonWriter.Rent(bounded) : null;
        var (status, failedAt, flags) = Observe(ref reader, input, sequence, (JsonWriter?)ignoreNulls ?? bounded, context, options, comments);
        if (status == MaskStatus.Unrecognized)
        {
            return new MaskResult { Status = MaskStatus.Unrecognized };
        }

        try
        {
            var written = bounded.CopyTo(output, status is MaskStatus.Masked or MaskStatus.Truncated && failedAt < 0);
            if (bounded.Exhausted)
            {
                flags |= MaskFlags.OutputCapped;
            }

            if (bounded.ValuesTruncated)
            {
                flags |= MaskFlags.ValueCut;
            }

            if (bounded.InvalidUtf8Replaced)
            {
                flags |= MaskFlags.InvalidUtf8Replaced;
            }

            if (status == MaskStatus.Masked && flags != MaskFlags.None)
            {
                status = MaskStatus.Truncated;
            }

            return new MaskResult { Status = status, BytesWritten = written, FailedAtByte = failedAt, Flags = flags };
        }
        catch (Exception)
        {
            return new MaskResult { Status = MaskStatus.Invalid, FailedAtByte = failedAt, Flags = flags };
        }
    }

    /// <summary>
    /// Hands values of a JSON text to <paramref name="context"/> without writing anything. Never throws:
    /// values after a problem in the text are not read, and the result says why.
    /// </summary>
    /// <param name="json">JSON text; it may be cut short.</param>
    /// <param name="context">Receives the values read rules extract.</param>
    /// <param name="options">Limits; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>Status and the UTF-8 offset where reading stopped; <see cref="MaskStatus.Unrecognized"/> for <c>null</c>.</returns>
    public MaskResult Read(string? json, TContext context, JsonObserverOptions? options = null)
    {
        if (json is null)
        {
            return new MaskResult { Status = MaskStatus.Unrecognized };
        }

        byte[]? input = null;
        try
        {
            input = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(json));
            return Read(input.AsSpan(0, Encoding.UTF8.GetBytes(json, input)), context, options);
        }
        catch (Exception)
        {
            return new MaskResult { Status = MaskStatus.Invalid };
        }
        finally
        {
            if (input is not null)
            {
                ArrayPool<byte>.Shared.Return(input, clearArray: true);
            }
        }
    }

    /// <summary>
    /// Hands values of a UTF-8 JSON payload to <paramref name="context"/> without writing anything. Never throws:
    /// values after a problem in the payload are not read, and the result says why.
    /// </summary>
    /// <param name="utf8">UTF-8 JSON payload; it may be cut short. A leading byte order mark is skipped.</param>
    /// <param name="context">Receives the values read rules extract.</param>
    /// <param name="options">Limits; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>Status and the input offset where reading stopped.</returns>
    public MaskResult Read(ReadOnlySpan<byte> utf8, TContext context, JsonObserverOptions? options = null)
    {
        options ??= JsonObserverOptions.Default;
        utf8 = SkipBom(utf8);
        var reader = CreateReader(utf8, options, comments: false);
        var (status, failedAt, flags) = Observe(ref reader, utf8, default, JsonWriter.Empty, context, options, null);
        return new MaskResult { Status = status, FailedAtByte = failedAt, Flags = flags };
    }

    /// <summary>
    /// Hands values of a UTF-8 JSON payload held in several buffers to <paramref name="context"/> without writing
    /// anything or copying the payload into one buffer. Never throws, and behaves exactly like
    /// <see cref="Read(ReadOnlySpan{byte}, TContext, JsonObserverOptions?)"/> for the same bytes, however they are split.
    /// </summary>
    /// <param name="utf8">UTF-8 JSON payload; it may be cut short. A leading byte order mark is skipped.</param>
    /// <param name="context">Receives the values read rules extract.</param>
    /// <param name="options">Limits; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>Status and the input offset where reading stopped.</returns>
    public MaskResult Read(in ReadOnlySequence<byte> utf8, TContext context, JsonObserverOptions? options = null)
    {
        if (utf8.IsSingleSegment)
        {
            return Read(utf8.FirstSpan, context, options);
        }

        options ??= JsonObserverOptions.Default;
        var reader = CreateReader(SkipBom(utf8), options, comments: false);
        var (status, failedAt, flags) = Observe(ref reader, default, default, JsonWriter.Empty, context, options, null);
        return new MaskResult { Status = status, FailedAtByte = failedAt, Flags = flags };
    }

    private static ReadOnlySpan<byte> SkipBom(ReadOnlySpan<byte> utf8) => utf8.StartsWith(Utf8Bom) ? utf8[Utf8Bom.Length..] : utf8;

    private static ReadOnlySequence<byte> SkipBom(in ReadOnlySequence<byte> utf8)
    {
        if (utf8.Length < Utf8Bom.Length)
        {
            return utf8;
        }

        Span<byte> head = stackalloc byte[3];
        utf8.Slice(0, Utf8Bom.Length).CopyTo(head);
        return head.SequenceEqual(Utf8Bom) ? utf8.Slice(Utf8Bom.Length) : utf8;
    }

    /// <summary>
    /// The comment policy of a writing call, or <c>null</c> when every comment would be dropped anyway, so that the
    /// reader skips them at no cost.
    /// </summary>
    private CommentPolicy? CommentsOf(JsonObserverOptions options)
    {
        var policy = options.Comments ?? CommentPolicy.AllowList;
        return policy.Kind == CommentPolicyKind.DropAll || (policy.Kind == CommentPolicyKind.AllowList && !_hasCommentRules) ? null : policy;
    }

    private static JsonReaderState ReaderState(JsonObserverOptions options, bool comments) => new(new JsonReaderOptions
    {
        CommentHandling = comments ? JsonCommentHandling.Allow : JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = Math.Max(options.MaxDepth, 1),
    });

    private static Utf8JsonReader CreateReader(ReadOnlySpan<byte> utf8, JsonObserverOptions options, bool comments) =>
        new(utf8, isFinalBlock: false, ReaderState(options, comments));

    private static Utf8JsonReader CreateReader(in ReadOnlySequence<byte> utf8, JsonObserverOptions options, bool comments) =>
        new(utf8, isFinalBlock: false, ReaderState(options, comments));

    private (MaskStatus Status, long FailedAt, MaskFlags Flags) Observe(
        ref Utf8JsonReader reader,
        ReadOnlySpan<byte> input,
        in ReadOnlySequence<byte> sequence,
        JsonWriter writer,
        TContext context,
        JsonObserverOptions options,
        CommentPolicy? comments)
    {
        var propPath = new JsonWalk(_maxDepth, input, sequence, options, comments);
        try
        {
            if (!ReadRoot(ref reader, ref propPath) || reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
            {
                return (MaskStatus.Unrecognized, 0, MaskFlags.None);
            }

            if (comments is not null)
            {
                JsonComments.Flush(writer, ref propPath, CommentKind.Before, null, ownerMasked: false);
            }

            _maskDelegate(ref reader, writer, context, 0, ref propPath, JsonValuePolicy<TContext>.Default.Rule);
            UpdateMaxDepth(propPath.MaxLength);
            if (writer.Stopped)
            {
                return (MaskStatus.Truncated, reader.BytesConsumed, MaskFlags.OutputCapped);
            }

            if (propPath.Stopped)
            {
                return (MaskStatus.Truncated, reader.BytesConsumed, MaskFlags.InputTruncated);
            }

            return HasTrailingData(ref reader, writer, ref propPath)
                ? (MaskStatus.Truncated, -1, MaskFlags.TrailingData)
                : (MaskStatus.Masked, -1, MaskFlags.None);
        }
        catch (Exception)
        {
            var depth = reader.CurrentDepth >= Math.Max(options.MaxDepth, 1) - 1 ? MaskFlags.Depth : MaskFlags.None;
            return (MaskStatus.Invalid, reader.BytesConsumed, depth);
        }
        finally
        {
            propPath.Dispose();
        }
    }

    /// <summary>
    /// Whether anything but whitespace follows the root, which the reader reports as a second token or an error.
    /// </summary>
    private static bool HasTrailingData(ref Utf8JsonReader reader, JsonWriter writer, ref JsonWalk walk)
    {
        var end = reader.BytesConsumed;
        var inline = true;
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.Comment)
                {
                    return true;
                }

                inline = inline && walk.IsSameLine(end, reader.TokenStartIndex);
                var kind = inline ? CommentKind.Inline : CommentKind.After;
                walk.Pending!.Add(ref reader, walk.StyleAt(reader.TokenStartIndex));
                JsonComments.Flush(writer, ref walk, kind, null, ownerMasked: false);
            }

            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>
    /// Reads the root token, keeping the comments before it.
    /// </summary>
    private static bool ReadRoot(ref Utf8JsonReader reader, ref JsonWalk walk)
    {
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.Comment)
            {
                return true;
            }

            walk.Pending!.Add(ref reader, walk.StyleAt(reader.TokenStartIndex));
        }

        return false;
    }

    /// <summary>
    /// Remembers the deepest path seen, so that later calls start with a large enough path buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateMaxDepth(int newMaxDepth)
    {
        if (_maxDepth >= newMaxDepth)
        {
            return;
        }

        int initialValue;
        do
        {
            initialValue = _maxDepth;
            if (newMaxDepth <= initialValue)
            {
                return;
            }
        } while (Interlocked.CompareExchange(ref _maxDepth, newMaxDepth, initialValue) != initialValue);
    }
}
