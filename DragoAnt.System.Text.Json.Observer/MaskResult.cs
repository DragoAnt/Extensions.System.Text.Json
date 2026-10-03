namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Outcome of masking or reading a JSON payload. Whatever the status, the output never holds a value a rule masks.
/// </summary>
public enum MaskStatus
{
    /// <summary>
    /// The whole payload was masked and every value was written in full.
    /// </summary>
    Masked,

    /// <summary>
    /// Part of the data is missing from the output, which is still valid JSON:
    /// the payload ended inside the document or the output reached <see cref="JsonObserverOptions.MaxOutputBytes"/>
    /// (the output holds the masked part with every open object and array closed, and <see cref="MaskResult.FailedAtByte"/>
    /// is where reading stopped), or a string longer than <see cref="JsonObserverOptions.MaxValueBytes"/> was cut
    /// (the whole document was read and <see cref="MaskResult.FailedAtByte"/> is -1).
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
/// Result of masking or reading a JSON payload.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="BytesWritten">UTF-8 bytes written to the output; 0 when only reading.</param>
/// <param name="FailedAtByte">
/// UTF-8 offset in the input where reading stopped; -1 when the whole payload was read, 0 for <see cref="MaskStatus.NotJson"/>.
/// </param>
public readonly record struct MaskResult(MaskStatus Status, int BytesWritten, long FailedAtByte);
