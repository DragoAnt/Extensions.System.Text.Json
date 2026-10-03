namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// Receives the entries of <see cref="JsonBodyLoggingHandler"/>. Register one in DI to replace the default
/// <see cref="LoggerJsonBodyLogSink"/>.
/// </summary>
/// <remarks>
/// <see cref="Write"/> may be called concurrently, and for a response it runs on a background thread, possibly after the response
/// was returned to the caller. An exception it throws is reported through the handler's logger and never reaches the caller.
/// </remarks>
public interface IJsonBodyLogSink
{
    /// <summary>
    /// Writes one entry.
    /// </summary>
    /// <param name="entry">The logged call; its bodies are already masked.</param>
    void Write(in JsonBodyLogEntry entry);
}
