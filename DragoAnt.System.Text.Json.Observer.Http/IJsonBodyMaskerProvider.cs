namespace DragoAnt.System.Text.Json.Observer.Http;

/// <summary>
/// Chooses the <see cref="JsonObserver"/> that masks a body. Register one in DI; without it every value is masked.
/// </summary>
/// <remarks>
/// Called for every logged body, possibly concurrently, so cache the observers. An exception it throws is reported through the
/// handler's logger and the body is logged as <c>[body not logged: masking failed]</c>.
/// </remarks>
public interface IJsonBodyMaskerProvider
{
    /// <summary>
    /// Returns the masker for a body.
    /// </summary>
    /// <param name="modelType">Model type from <see cref="JsonBodyLoggingContext"/> or the options defaults, or <c>null</c> when unknown.</param>
    /// <param name="clientName">Name of the <see cref="HttpClient"/> that made the call.</param>
    /// <returns>The masker, or <c>null</c> to log the body as <c>[body withheld]</c>.</returns>
    JsonObserver? GetMasker(Type? modelType, string clientName);
}
