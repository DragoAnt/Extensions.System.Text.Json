namespace DragoAnt.System.Text.Json.Observer.Http;

public sealed class JsonBodyLoggingOptions
{
    public JsonBodyLogWhen When { get; set; } = JsonBodyLogWhen.OnFailure;
    public int MaxBodyBytes { get; set; } = 4096;
    public bool IncludeSensitive { get; set; } = false;
    public Type? DefaultRequestType { get; set; }
    public Type? DefaultResponseType { get; set; }
}
