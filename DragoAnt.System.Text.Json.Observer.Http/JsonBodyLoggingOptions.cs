namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// Settings of <see cref="JsonBodyLoggingHandler"/>. With <see cref="HttpClientBuilderExtensions.AddJsonBodyLogging"/> they are named
/// options per client name, read on every call; use <c>services.ConfigureAll&lt;JsonBodyLoggingOptions&gt;(...)</c> to change every client.
/// </summary>
public sealed class JsonBodyLoggingOptions
{
    /// <summary>
    /// Which calls are logged. Default: <see cref="JsonBodyLogWhen.OnFailure"/>.
    /// </summary>
    public JsonBodyLogWhen When { get; set; } = JsonBodyLogWhen.OnFailure;

    /// <summary>
    /// Most bytes of each body that are read and logged; a longer body is logged truncated. 0 or less turns body logging off
    /// while calls are still logged. Default: 4096.
    /// </summary>
    public int MaxBodyBytes { get; set; } = 4096;

    /// <summary>
    /// Options for masking each body. The hash key, hash key id, value limit, depth limit, strategy, null handling,
    /// indentation, relaxed escaping and name matching pass through. <see cref="ObserverOptions.MaxOutputBytes"/> is capped at
    /// <see cref="MaxBodyBytes"/>, with a non-positive value treated as no additional cap, and comments are always dropped.
    /// Default: <see cref="JsonObserverOptions.Default"/>.
    /// </summary>
    public JsonObserverOptions? MaskOptions { get; set; }

    /// <summary>
    /// Log bodies as is, without masking. Every such entry is marked <see cref="JsonBodyLogEntry.BodyUnmasked"/>, and the handler
    /// logs a warning the first time it does so. Meant for local debugging only. Default: <c>false</c>.
    /// </summary>
    public bool IncludeSensitive { get; set; }

    /// <summary>
    /// Model type passed to <see cref="IJsonBodyMaskerProvider.GetMasker"/> for a request body when the request carries no
    /// <see cref="JsonBodyLoggingContext"/>.
    /// </summary>
    public Type? DefaultRequestType { get; set; }

    /// <summary>
    /// Model type passed to <see cref="IJsonBodyMaskerProvider.GetMasker"/> for a response body when the request carries no
    /// <see cref="JsonBodyLoggingContext"/>.
    /// </summary>
    public Type? DefaultResponseType { get; set; }
}
