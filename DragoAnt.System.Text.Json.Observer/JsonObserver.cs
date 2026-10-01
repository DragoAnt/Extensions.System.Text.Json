using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using DragoAnt.System.Text.Json.Observer.Builders;

// ReSharper disable MemberCanBePrivate.Global

namespace DragoAnt.System.Text.Json.Observer;

public sealed class JsonObserver
{
    private readonly JsonObserver<JsonObserveringEmptyContext> _masking;

    /// <summary>
    /// Mask any - object or array.
    /// </summary>
    /// <param name="initObj">Init masking for object.</param>
    /// <param name="initArray">Init masking for array.</param>
    /// <param name="defaultMasking">Default masking for unknown scenarios.</param>
    public static JsonObserver Any(
        Action<JsonObjBuilder<JsonObserveringEmptyContext>> initObj,
        Action<JsonArrayBuilder<JsonObserveringEmptyContext>> initArray,
        JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultMasking = null) =>
        new(Any<JsonObserveringEmptyContext>(initObj, initArray, defaultMasking));

    /// <summary>
    /// Mask object.
    /// </summary>
    /// <param name="defaultMasking">Default masking for unknown scenarios.</param>
    public static JsonObserver Obj(JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultMasking) =>
        new(Obj<JsonObserveringEmptyContext>(defaultMasking));

    /// <summary>
    /// Mask object.
    /// </summary>
    /// <param name="init">Init masking.</param>
    /// <param name="defaultMasking">Default masking for unknown scenarios.</param>
    public static JsonObserver Obj(
        Action<JsonObjBuilder<JsonObserveringEmptyContext>> init,
        JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultMasking = null) =>
        new(Obj<JsonObserveringEmptyContext>(init, defaultMasking));

    /// <summary>
    /// Mask array.
    /// </summary>
    /// <param name="defaultMasking">Default masking for unknown scenarios.</param>
    public static JsonObserver Array(JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultMasking) =>
        new(Array<JsonObserveringEmptyContext>(defaultMasking));

    /// <summary>
    /// Mask array.
    /// </summary>
    /// <param name="init">Init masking.</param>
    /// <param name="defaultMasking">Default masking for unknown scenarios.</param>
    public static JsonObserver Array(
        Action<JsonArrayBuilder<JsonObserveringEmptyContext>> init,
        JsonObserverValueDelegate<JsonObserveringEmptyContext>? defaultMasking = null) =>
        new(Array<JsonObserveringEmptyContext>(init, defaultMasking));

    /// <summary>
    /// Mask any - object or array.
    /// </summary>
    /// <param name="initObj">Init masking for object.</param>
    /// <param name="initArray">Init masking for array.</param>
    /// <param name="defaultMasking">Default masking for unknown scenarios.</param>
    public static JsonObserver<TContext> Any<TContext>(
        Action<JsonObjBuilder<TContext>> initObj,
        Action<JsonArrayBuilder<TContext>> initArray,
        JsonObserverValueDelegate<TContext>? defaultMasking = null) =>
        new(JsonObserverItem<TContext>.Any(initObj, initArray, defaultMasking));

    /// <summary>
    /// Mask object.
    /// </summary>
    /// <param name="defaultMasking">Default masking for unknown scenarios.</param>
    public static JsonObserver<TContext> Obj<TContext>(JsonObserverValueDelegate<TContext>? defaultMasking) =>
        new(JsonObserverItem<TContext>.Obj(_ => { }, defaultMasking));

    /// <summary>
    /// Mask object.
    /// </summary>
    /// <param name="init">Init masking.</param>
    /// <param name="defaultMasking">Default masking for unknown scenarios.</param>
    public static JsonObserver<TContext> Obj<TContext>(
        Action<JsonObjBuilder<TContext>> init,
        JsonObserverValueDelegate<TContext>? defaultMasking = null) =>
        new(JsonObserverItem<TContext>.Obj(init, defaultMasking));

    /// <summary>
    /// Mask array.
    /// </summary>
    /// <param name="defaultMasking">Default masking for unknown scenarios.</param>
    public static JsonObserver<TContext> Array<TContext>(JsonObserverValueDelegate<TContext>? defaultMasking) =>
        new(JsonObserverItem<TContext>.Array(_ => { }, defaultMasking));

    /// <summary>
    /// Mask array.
    /// </summary>
    /// <param name="init">Init masking.</param>
    /// <param name="defaultMasking">Default masking for unknown scenarios.</param>
    public static JsonObserver<TContext> Array<TContext>(
        Action<JsonArrayBuilder<TContext>> init,
        JsonObserverValueDelegate<TContext>? defaultMasking = null) =>
        new(JsonObserverItem<TContext>.Array(init, defaultMasking));

    private JsonObserver(JsonObserver<JsonObserveringEmptyContext> masking)
    {
        _masking = masking;
    }

    /// <summary>
    /// JSON-string masking using defined strategies.
    /// </summary>
    /// <param name="value">Value for masking.</param>
    /// <param name="readerOptions">JSON reader options.</param>
    /// <param name="writerOptions">JSON writer options.</param>
    /// <param name="ignoreNulls">Ignore null properties.</param>
    /// <param name="ignoreComments">Ignore comments.</param>
    /// <returns>Masked JSON-string.</returns>
    public string? Mask(
        string? value,
        JsonReaderOptions readerOptions = default,
        JsonWriterOptions writerOptions = default,
        bool ignoreNulls = false,
        bool ignoreComments = false)
        => _masking.Mask(value, JsonObserveringEmptyContext.Instance, readerOptions, writerOptions, ignoreNulls, ignoreComments);

    /// <summary>
    /// Masks a UTF-8 JSON payload into <paramref name="output"/>. Never throws: problems are reported in the result,
    /// and only masked values are ever written.
    /// </summary>
    /// <param name="utf8">UTF-8 JSON payload; it may be cut short, for example by a size limit.</param>
    /// <param name="output">Receives the masked JSON.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    public MaskResult Mask(ReadOnlySpan<byte> utf8, IBufferWriter<byte> output, JsonObserverOptions? options = null)
        => _masking.Mask(utf8, output, JsonObserveringEmptyContext.Instance, options);
}

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
    /// JSON-string masking using defined strategies.
    /// </summary>
    /// <param name="value">Value for masking.</param>
    /// <param name="context">Masking context.</param>
    /// <param name="readerOptions">JSON reader options.</param>
    /// <param name="writerOptions">JSON writer options.</param>
    /// <param name="ignoreNulls">Ignore null properties.</param>
    /// <param name="ignoreComments">Ignore comments; they are also accepted in the input then.</param>
    /// <returns>Masked JSON-string.</returns>
    public string? Mask(
        string? value,
        TContext context,
        JsonReaderOptions readerOptions = default,
        JsonWriterOptions writerOptions = default,
        bool ignoreNulls = false,
        bool ignoreComments = false)
    {
        if (value is null)
        {
            return null;
        }

        if (ignoreComments && readerOptions.CommentHandling == JsonCommentHandling.Disallow)
        {
            readerOptions.CommentHandling = JsonCommentHandling.Skip;
        }

        var input = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(value.Length));
        try
        {
            var utf8 = input.AsSpan(0, Encoding.UTF8.GetBytes(value, input));
            using var output = new PooledBufferWriter(Math.Max(utf8.Length, 256));
            using (var writer = new Utf8JsonWriter(output, writerOptions))
            {
                var reader = new Utf8JsonReader(utf8, readerOptions);
                MaskStrict(ref reader, utf8, context, JsonWriter.FromUtf8JsonWriter(writer, ignoreNulls, ignoreComments));
                writer.Flush();
            }

            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(input);
        }
    }

    /// <summary>
    /// Masks a UTF-8 JSON payload into <paramref name="output"/>. Never throws: problems are reported in the result,
    /// and only masked values are ever written.
    /// </summary>
    /// <param name="utf8">UTF-8 JSON payload; it may be cut short, for example by a size limit.</param>
    /// <param name="output">Receives the masked JSON.</param>
    /// <param name="context">Masking context.</param>
    /// <param name="options">Limits and output settings; <see cref="JsonObserverOptions.Default"/> when omitted.</param>
    public MaskResult Mask(ReadOnlySpan<byte> utf8, IBufferWriter<byte> output, TContext context, JsonObserverOptions? options = null)
    {
        options ??= JsonObserverOptions.Default;
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
        using var writer = new BoundedJsonWriter(options);
        var propPath = new PropertyPath(_maxDepth, utf8);
        var status = MaskStatus.Masked;
        long failedAt = -1;
        try
        {
            if (!reader.Read() || reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
            {
                return new MaskResult(MaskStatus.NotJson, 0, 0);
            }

            _maskDelegate(ref reader, writer, context, 0, ref propPath, JsonObserverValuePolicies<TContext>.Default);
            UpdateMaxDepth(propPath.MaxLength);
            if (propPath.Stopped || writer.Exhausted)
            {
                status = MaskStatus.Truncated;
                failedAt = reader.BytesConsumed;
            }
        }
        catch (Exception)
        {
            status = MaskStatus.Invalid;
            failedAt = reader.BytesConsumed;
        }
        finally
        {
            propPath.Dispose();
        }

        try
        {
            return new MaskResult(status, writer.CopyTo(output, status == MaskStatus.Masked), failedAt);
        }
        catch (Exception)
        {
            return new MaskResult(MaskStatus.Invalid, 0, failedAt);
        }
    }

    private void MaskStrict(ref Utf8JsonReader reader, ReadOnlySpan<byte> input, TContext context, JsonWriter jsonWriter)
    {
        reader.Read();
        var propPath = new PropertyPath(_maxDepth, input);
        try
        {
            _maskDelegate(ref reader, jsonWriter, context, 0, ref propPath, JsonObserverValuePolicies<TContext>.Default);
            UpdateMaxDepth(propPath.MaxLength);
        }
        finally
        {
            propPath.Dispose();
        }
    }

    /// <summary>
    /// Update max depth for optimize next calls.
    /// </summary>
    /// <param name="newMaxDepth"></param>
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

    /// <summary>
    /// Reads values to context from JSON-string by using defined strategies.
    /// </summary>
    /// <param name="value">Value for masking.</param>
    /// <param name="context">Masking context.</param>
    /// <param name="readerOptions">JSON reader options.</param>
    /// <returns>Masked JSON-string.</returns>
    public void Read(string? value, TContext context, JsonReaderOptions readerOptions = default)
    {
        if (value is null)
        {
            return;
        }

        Read(Encoding.UTF8.GetBytes(value), context, readerOptions);
    }

    /// <summary>
    /// Reads values to context from JSON-string by using defined strategies.
    /// </summary>
    /// <param name="utf8Bytes">Value for masking.</param>
    /// <param name="context">Masking context.</param>
    /// <param name="readerOptions">JSON reader options.</param>
    /// <returns>Masked JSON-string.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Read(byte[] utf8Bytes, TContext context, JsonReaderOptions readerOptions = default)
    {
        var reader = new Utf8JsonReader(utf8Bytes, readerOptions);
        MaskStrict(ref reader, utf8Bytes, context, JsonWriter.Empty);
    }
}
