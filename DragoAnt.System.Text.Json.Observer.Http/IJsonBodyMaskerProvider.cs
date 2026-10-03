namespace DragoAnt.System.Text.Json.Observer.Http;

public interface IJsonBodyMaskerProvider
{
    JsonObserver? GetMasker(Type? modelType, string clientName);
}
