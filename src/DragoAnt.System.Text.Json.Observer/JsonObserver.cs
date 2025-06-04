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
}

public sealed class JsonObserver<TContext>
{
    private readonly JsonObserverDelegate<TContext> _maskDelegate;
    private int _maxDepth = 6;

    internal JsonObserver(JsonObserverDelegate<TContext> maskDelegate)
    {
        _maskDelegate = maskDelegate;
    }

    /// <summary>
    /// JSON-string masking using defined strategies.
    /// </summary>
    /// <param name="value">Value for masking.</param>
    /// <param name="context">Masking context.</param>
    /// <param name="readerOptions">JSON reader options.</param>
    /// <param name="writerOptions">JSON writer options.</param>
    /// <param name="ignoreNulls">Ignore null properties.</param>
    /// <param name="ignoreComments">Ignore comments.</param>
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

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(value), readerOptions);
        using var stream = new MemoryStream();

        Mask(reader, context, stream, writerOptions, ignoreNulls, ignoreComments);

        stream.Flush();
        stream.Seek(0, SeekOrigin.Begin);

        using var streamReader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var maskedJson = streamReader.ReadToEnd();

        return maskedJson;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Mask(
        Utf8JsonReader reader,
        TContext context,
        Stream output,
        JsonWriterOptions writerOptions = default,
        bool ignoreNulls = false,
        bool ignoreComments = false)
    {
        using var writer = new Utf8JsonWriter(output, writerOptions);
        var jsonWriter = JsonWriter.FromUtf8JsonWriter(writer, ignoreNulls, ignoreComments);

        Mask(reader, context, jsonWriter);

        writer.Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Mask(Utf8JsonReader reader, TContext context, JsonWriter jsonWriter)
    {
        reader.Read();
        var propPath = new PropertyPath(_maxDepth);
        _maskDelegate(ref reader, jsonWriter, context, 0, ref propPath, JsonObserverValuePolicies<TContext>.Default);

        var newMaxDepth = propPath.MaxLength;
        UpdateMaxDepth(newMaxDepth);
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
        Mask(reader, context, JsonWriter.Empty);
    }
}