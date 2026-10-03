namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// Which HTTP calls <see cref="JsonBodyLoggingHandler"/> logs.
/// </summary>
public enum JsonBodyLogWhen
{
    /// <summary>
    /// Log nothing.
    /// </summary>
    Never,

    /// <summary>
    /// Log calls that end with a non-success status code, an exception or a cancellation (which includes <see cref="HttpClient.Timeout"/>).
    /// </summary>
    OnFailure,

    /// <summary>
    /// Log every call.
    /// </summary>
    Always,
}
