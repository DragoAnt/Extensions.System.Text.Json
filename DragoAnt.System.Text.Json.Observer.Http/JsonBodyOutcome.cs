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
    /// The call was canceled before a response arrived. This covers both a canceled caller token and
    /// <see cref="HttpClient.Timeout"/>: the handler receives them as one token and cannot tell them apart.
    /// </summary>
    Canceled,
}
