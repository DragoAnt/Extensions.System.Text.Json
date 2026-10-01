namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Outcome of masking a UTF-8 JSON payload.
/// </summary>
public enum MaskStatus
{
    /// <summary>
    /// The whole payload was masked.
    /// </summary>
    Masked,

    /// <summary>
    /// The payload ended inside the document, or the output reached its size limit.
    /// The output holds the masked part, with every open object and array closed.
    /// </summary>
    Truncated,

    /// <summary>
    /// The payload is empty or its root is not an object or an array. Nothing was written.
    /// </summary>
    NotJson,

    /// <summary>
    /// The payload is not valid JSON, or a rule failed. The output holds the masked part read before the failure,
    /// with every open object and array closed; nothing after the failure is written.
    /// </summary>
    Invalid,
}

/// <summary>
/// Result of masking a UTF-8 JSON payload.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="BytesWritten">Bytes written to the output.</param>
/// <param name="FailedAtByte">Input offset where reading stopped, or -1 when the whole payload was masked.</param>
public readonly record struct MaskResult(MaskStatus Status, int BytesWritten, long FailedAtByte);
