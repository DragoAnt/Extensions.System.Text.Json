namespace DragoAnt.System.Text.Json.Observer.Http;

public sealed record JsonBodyLoggingContext(Type? RequestType, Type? ResponseType, string? Operation = null);
