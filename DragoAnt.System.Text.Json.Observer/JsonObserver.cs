using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using DragoAnt.System.Text.Json.Observer.Builders;

// ReSharper disable MemberCanBePrivate.Global

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Masks JSON in one streaming pass from <see cref="Utf8JsonReader"/> to <see cref="Utf8JsonWriter"/>, without deserializing it.
/// Build it once with a factory method and share it: it is thread-safe.
/// </summary>
public sealed class JsonObserver
{
    private readonly JsonObserver<JsonObserveringEmptyContext> _masking;

    /// <summary>
    /// Creates an observer for a payload whose root is an object or an array.
    /// </summary>
    /// <param name="initObj">Adds the rules for a root object.</param>
    /// <param name="initArray">Adds the rules for a root array.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="JsonObserverValuePolicies.AllowList"/> when <c>null</c>.</param>
    public static JsonObserver Any(
        Action<JsonObjBuilder<JsonObserveringEmptyContext>> initObj,
        Action<JsonArrayBuilder<JsonObserveringEmptyContext>> initArray,
        JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultMasking = null) =>
        new(Any<JsonObserveringEmptyContext>(initObj, initArray, defaultMasking));

    /// <summary>
    /// Creates an observer for a root object that applies one policy to every value, for example
    /// <see cref="JsonObserverValuePolicies.Relative"/>. A root array is <see cref="MaskStatus.Invalid"/>.
    /// </summary>
    /// <param name="defaultMasking">Policy for every value; <see cref="JsonObserverValuePolicies.AllowList"/> when <c>null</c>.</param>
    public static JsonObserver Obj(JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultMasking) =>
        new(Obj<JsonObserveringEmptyContext>(defaultMasking));

    /// <summary>
    /// Creates an observer for a root object. A root array is <see cref="MaskStatus.Invalid"/>; use <see cref="Any"/> for both.
    /// </summary>
    /// <param name="init">Adds the rules for the object's properties.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="JsonObserverValuePolicies.AllowList"/> when <c>null</c>.</param>
    public static JsonObserver Obj(
        Action<JsonObjBuilder<JsonObserveringEmptyContext>> init,
        JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultMasking = null) =>
        new(Obj<JsonObserveringEmptyContext>(init, defaultMasking));

    /// <summary>
    /// Creates an observer for a root array that applies one policy to every value. A root object is <see cref="MaskStatus.Invalid"/>.
    /// </summary>
    /// <param name="defaultMasking">Policy for every value; <see cref="JsonObserverValuePolicies.AllowList"/> when <c>null</c>.</param>
    public static JsonObserver Array(JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultMasking) =>
        new(Array<JsonObserveringEmptyContext>(defaultMasking));

    /// <summary>
    /// Creates an observer for a root array. A root object is <see cref="MaskStatus.Invalid"/>; use <see cref="Any"/> for both.
    /// </summary>
    /// <param name="init">Adds the rules for the array's items.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="JsonObserverValuePolicies.AllowList"/> when <c>null</c>.</param>
    public static JsonObserver Array(
        Action<JsonArrayBuilder<JsonObserveringEmptyContext>> init,
        JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultMasking = null) =>
        new(Array<JsonObserveringEmptyContext>(init, defaultMasking));

    /// <summary>
    /// Creates an observer that also extracts values into a <typeparamref name="TContext"/>, for a root object or array.
    /// </summary>
    /// <param name="initObj">Adds the rules for a root object.</param>
    /// <param name="initArray">Adds the rules for a root array.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="JsonObserverValuePolicies{TContext}.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    public static JsonObserver<TContext> Any<TContext>(
        Action<JsonObjBuilder<TContext>> initObj,
        Action<JsonArrayBuilder<TContext>> initArray,
        JsonObserverValueDelegate<TContext>? defaultMasking = null) =>
        new(JsonObserverItem<TContext>.Any(initObj, initArray, defaultMasking));

    /// <summary>
    /// Creates an observer with a context for a root object that applies one policy to every value.
    /// </summary>
    /// <param name="defaultMasking">Policy for every value; <see cref="JsonObserverValuePolicies{TContext}.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    public static JsonObserver<TContext> Obj<TContext>(JsonObserverValueDelegate<TContext>? defaultMasking) =>
        new(JsonObserverItem<TContext>.Obj(_ => { }, defaultMasking));

    /// <summary>
    /// Creates an observer that also extracts values into a <typeparamref name="TContext"/>, for a root object.
    /// </summary>
    /// <param name="init">Adds the rules for the object's properties.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="JsonObserverValuePolicies{TContext}.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    public static JsonObserver<TContext> Obj<TContext>(
        Action<JsonObjBuilder<TContext>> init,
        JsonObserverValueDelegate<TContext>? defaultMasking = null) =>
        new(JsonObserverItem<TContext>.Obj(init, defaultMasking));

    /// <summary>
    /// Creates an observer with a context for a root array that applies one policy to every value.
    /// </summary>
    /// <param name="defaultMasking">Policy for every value; <see cref="JsonObserverValuePolicies{TContext}.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    public static JsonObserver<TContext> Array<TContext>(JsonObserverValueDelegate<TContext>? defaultMasking) =>
        new(JsonObserverItem<TContext>.Array(_ => { }, defaultMasking));

    /// <summary>
    /// Creates an observer that also extracts values into a <typeparamref name="TContext"/>, for a root array.
    /// </summary>
    /// <param name="init">Adds the rules for the array's items.</param>
    /// <param name="defaultMasking">Policy for values no rule matches; <see cref="JsonObserverValuePolicies{TContext}.AllowList"/> when <c>null</c>.</param>
    /// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
    public static JsonObserver<TContext> Array<TContext>(
        Action<JsonArrayBuilder<TContext>> init,
        JsonObserverValueDelegate<TContext>? defaultMasking = null) =>
        new(JsonObserverItem<TContext>.Array(init, defaultMasking));

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
        return new JsonObserver(new JsonObserver<JsonObserveringEmptyContext>(walker.Invoke));
    }

    private JsonObserver(JsonObserver<JsonObserveringEmptyContext> masking)
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
        => _masking.Mask(json, JsonObserveringEmptyContext.Instance, out _, options);

    /// <summary>
    /// Masks a JSON text and reports what happened. Never throws: invalid or cut-off JSON yields its safe masked prefix,
    /// and only masked values are ever written.
    /// </summary>
    /// <param name="json">JSON text; it may be cut short, for example by a size limit.</param>
    /// <param name="result">Status, UTF-8 length of the output and UTF-8 offset where reading stopped.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>The masked JSON; empty when the text is not a JSON object or array; <c>null</c> for <c>null</c>.</returns>
    public string? Mask(string? json, out MaskResult result, JsonObserverOptions? options = null)
        => _masking.Mask(json, JsonObserveringEmptyContext.Instance, out result, options);

    /// <summary>
    /// Masks a UTF-8 JSON payload into <paramref name="output"/>. Never throws: problems are reported in the result,
    /// and only masked values are ever written.
    /// </summary>
    /// <param name="utf8">UTF-8 JSON payload; it may be cut short, for example by a size limit. A leading byte order mark is skipped.</param>
    /// <param name="output">Receives the masked JSON.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>Status, bytes written and the input offset where reading stopped.</returns>
    public MaskResult Mask(ReadOnlySpan<byte> utf8, IBufferWriter<byte> output, JsonObserverOptions? options = null)
        => _masking.Mask(utf8, output, JsonObserveringEmptyContext.Instance, options);
}

/// <summary>
/// Masks JSON and hands values to a context of type <typeparamref name="TContext"/> in one streaming pass.
/// Build it once and share it: it is thread-safe.
/// </summary>
/// <typeparam name="TContext">Type that read rules write extracted values to.</typeparam>
public sealed class JsonObserver<TContext>
{
    private readonly JsonObserverDelegate<TContext> _maskDelegate;
    private int _maxDepth = 6;

    internal JsonObserver(JsonObserverDelegate<TContext> maskDelegate)
    {
        _maskDelegate = maskDelegate;
    }

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
            result = new MaskResult(MaskStatus.NotJson, 0, 0);
            return null;
        }

        byte[]? input = null;
        try
        {
            input = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(json));
            var utf8 = input.AsSpan(0, Encoding.UTF8.GetBytes(json, input));
            using var output = new PooledBufferWriter(Math.Clamp(utf8.Length, 256, 64 * 1024));
            result = Mask(utf8, output, context, options);
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
        catch (Exception)
        {
            result = new MaskResult(MaskStatus.Invalid, 0, 0);
            return string.Empty;
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
        using var bounded = new BoundedJsonWriter(options);
        using var ignoreNulls = options.IgnoreNulls ? new IgnoreNullsJsonWriter(bounded) : null;
        var (status, failedAt) = Observe(utf8, (JsonWriter?)ignoreNulls ?? bounded, context, options);
        if (status == MaskStatus.NotJson)
        {
            return new MaskResult(MaskStatus.NotJson, 0, 0);
        }

        try
        {
            var written = bounded.CopyTo(output, status == MaskStatus.Masked);
            if (status == MaskStatus.Masked && bounded.ValuesTruncated)
            {
                status = MaskStatus.Truncated;
            }

            return new MaskResult(status, written, failedAt);
        }
        catch (Exception)
        {
            return new MaskResult(MaskStatus.Invalid, 0, failedAt);
        }
    }

    /// <summary>
    /// Hands values of a JSON text to <paramref name="context"/> without writing anything. Never throws:
    /// values after a problem in the text are not read, and the result says why.
    /// </summary>
    /// <param name="json">JSON text; it may be cut short.</param>
    /// <param name="context">Receives the values read rules extract.</param>
    /// <param name="options">Limits; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    /// <returns>Status and the UTF-8 offset where reading stopped; <see cref="MaskStatus.NotJson"/> for <c>null</c>.</returns>
    public MaskResult Read(string? json, TContext context, JsonObserverOptions? options = null)
    {
        if (json is null)
        {
            return new MaskResult(MaskStatus.NotJson, 0, 0);
        }

        byte[]? input = null;
        try
        {
            input = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(json));
            return Read(input.AsSpan(0, Encoding.UTF8.GetBytes(json, input)), context, options);
        }
        catch (Exception)
        {
            return new MaskResult(MaskStatus.Invalid, 0, 0);
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
        var (status, failedAt) = Observe(utf8, JsonWriter.Empty, context, options ?? JsonObserverOptions.Default);
        return new MaskResult(status, 0, failedAt);
    }

    private (MaskStatus Status, long FailedAt) Observe(ReadOnlySpan<byte> utf8, JsonWriter writer, TContext context, JsonObserverOptions options)
    {
        if (utf8.StartsWith(Utf8Bom))
        {
            utf8 = utf8[Utf8Bom.Length..];
        }

        var reader = new Utf8JsonReader(utf8, isFinalBlock: false, new JsonReaderState(new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = Math.Max(options.MaxDepth, 1),
        }));
        var propPath = new PropertyPath(_maxDepth, utf8);
        try
        {
            if (!reader.Read() || reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
            {
                return (MaskStatus.NotJson, 0);
            }

            _maskDelegate(ref reader, writer, context, 0, ref propPath, JsonObserverValuePolicies<TContext>.Default);
            UpdateMaxDepth(propPath.MaxLength);
            return propPath.Stopped || writer.Stopped
                ? (MaskStatus.Truncated, reader.BytesConsumed)
                : (MaskStatus.Masked, -1);
        }
        catch (Exception)
        {
            return (MaskStatus.Invalid, reader.BytesConsumed);
        }
        finally
        {
            propPath.Dispose();
        }
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
