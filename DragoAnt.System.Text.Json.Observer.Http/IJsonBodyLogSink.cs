namespace DragoAnt.System.Text.Json.Observer.Http;

public interface IJsonBodyLogSink
{
    void Write(in JsonBodyLogEntry entry);
}
