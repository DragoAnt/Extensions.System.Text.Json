namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// How an HTTP call ended, as recorded in a <see cref="JsonBodyLogEntry"/>.
/// </summary>
public enum JsonBodyOutcome
{
    /// <summary>
    /// A response with a success status code (2xx) arrived.
    /// </summary>
    Success,

    /// <summary>
    /// A response with a non-success status code arrived.
    /// </summary>
    Failure,

    /// <summary>
    /// The call threw before a response arrived, for example a connection failure.
    /// </summary>
    Exception,

    /// <summary>
    /// <see cref="HttpClient.Timeout"/> elapsed for a call through
    /// <see cref="JsonBodyLogging.SendWithBodyLoggingAsync{TRequest,TResponse}"/> while the caller's token was not canceled.
    /// Every other cancellation, and any call when <see cref="HttpClient.Timeout"/> is infinite, is <see cref="Canceled"/>.
    /// </summary>
    Timeout,

    /// <summary>
    /// The call was canceled before a response arrived. A timeout sent without a helper-owned deadline is also
    /// recorded as canceled because the handler cannot distinguish it from another cancellation source.
    /// </summary>
    Canceled,
}
