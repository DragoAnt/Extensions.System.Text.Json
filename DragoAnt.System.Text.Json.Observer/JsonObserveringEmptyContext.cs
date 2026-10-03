namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Context of an observer that only masks and extracts nothing, such as every <see cref="JsonObserver"/>.
/// </summary>
public sealed class JsonObserveringEmptyContext
{
    /// <summary>
    /// The only instance.
    /// </summary>
    public static readonly JsonObserveringEmptyContext Instance = new();

    private JsonObserveringEmptyContext()
    {
    }
}
