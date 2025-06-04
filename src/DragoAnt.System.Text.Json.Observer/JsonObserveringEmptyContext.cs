namespace Extensions.System.Text.Json.Observer;

public sealed class JsonObserveringEmptyContext
{
    public static readonly JsonObserveringEmptyContext Instance = new();

    private JsonObserveringEmptyContext()
    {
    }
}