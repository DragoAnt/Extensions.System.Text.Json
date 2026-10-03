namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// What a logged request or response body contains. When no JSON could be written, the body text is a bracketed marker
/// such as <c>[body not JSON]</c> instead.
/// </summary>
public enum JsonBodyStatus
{
    /// <summary>
    /// The message has no body.
    /// </summary>
    None,

    /// <summary>
    /// The whole body was masked.
    /// </summary>
    Masked,

    /// <summary>
    /// The body was longer than <see cref="JsonBodyLoggingOptions.MaxBodyBytes"/> or ended inside the document;
    /// the masked part is logged, with every open object and array closed.
    /// </summary>
    Truncated,

    /// <summary>
    /// The body is not valid JSON. The masked part read before the error is logged, or <c>[invalid JSON]</c> when there is none.
    /// </summary>
    Invalid,

    /// <summary>
    /// The body is empty JSON or its root is not an object or an array, for example plain text; logged as <c>[body not JSON]</c>.
    /// </summary>
    NotJson,

    /// <summary>
    /// The body was logged as is, without masking, because <see cref="JsonBodyLoggingOptions.IncludeSensitive"/> is on.
    /// </summary>
    Raw,

    /// <summary>
    /// The <see cref="IJsonBodyMaskerProvider"/> returned no masker; logged as <c>[body withheld]</c>.
    /// </summary>
    Withheld,

    /// <summary>
    /// The body was not read: its media type, charset or content encoding is not supported, or
    /// <see cref="JsonBodyLoggingOptions.MaxBodyBytes"/> is 0. The marker names the reason, for example <c>[body not logged: text/plain]</c>.
    /// </summary>
    Skipped,

    /// <summary>
    /// The request body is a stream that cannot be read twice; logged as <c>[body not buffered]</c>.
    /// </summary>
    NotBuffered,

    /// <summary>
    /// The response body stopped before it was fully read for logging: the read failed, or the caller disposed the response first.
    /// The masked part received so far is logged, or <c>[body incomplete]</c> when there is none.
    /// </summary>
    Incomplete,

    /// <summary>
    /// Masking failed, for example the <see cref="IJsonBodyMaskerProvider"/> threw; logged as <c>[body not logged: masking failed]</c>.
    /// </summary>
    Failed,
}
