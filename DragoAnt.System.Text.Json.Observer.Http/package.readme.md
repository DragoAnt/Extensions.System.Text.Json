# DragoAnt.System.Text.Json.Observer.Http

Logs masked JSON request and response bodies of an `HttpClient`, using [DragoAnt.System.Text.Json.Observer](https://www.nuget.org/packages/DragoAnt.System.Text.Json.Observer) to mask them.

## Getting started

```sh
dotnet add package DragoAnt.System.Text.Json.Observer.Http
```

Targets `net8.0`, `net9.0` and `net10.0`.

## Usage

```csharp
using System.Net.Http.Json;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;

services.AddSingleton<IJsonBodyMaskerProvider, PaymentMaskers>();
services.AddHttpClient("PaymentApi", client => client.BaseAddress = new Uri("https://api.example.com/"))
    .AddJsonBodyLogging(options =>
    {
        options.When = JsonBodyLogWhen.OnFailure;
        options.MaxBodyBytes = 8 * 1024;
    });

// At the call site: attach the body model types and an operation name.
using var request = new HttpRequestMessage(HttpMethod.Post, "charges")
{
    Content = JsonContent.Create(charge),
}.WithBodyLogging<ChargeRequest, ChargeResponse>("Charge");
using var response = await httpClient.SendAsync(request, cancellationToken);

public sealed class PaymentMaskers : IJsonBodyMaskerProvider
{
    private static readonly JsonObserver Charge = JsonObserver.Obj(rules => rules.Match("cardNumber").Mask("****", MaskNulls.Mask));

    // null logs the body as "[body withheld]".
    public JsonObserver? GetMasker(Type? modelType, string clientName) =>
        modelType == typeof(ChargeRequest) ? Charge : null;
}
```

Without an `IJsonBodyMaskerProvider` every value of an object or array body is masked. Each logged call becomes one `JsonBodyLogEntry`, written by an `IJsonBodyLogSink`; the default sink writes one structured `ILogger` message, at `Information` for a success and `Warning` otherwise. Register your own `IJsonBodyLogSink` to send entries elsewhere.

## Behaviour

- **Which calls** — `When` is `Never`, `OnFailure` (the default: a non-success status code, an exception or a cancellation) or `Always`. A canceled caller token and `HttpClient.Timeout` reach the handler as one token, so both are logged with the outcome `Canceled`.
- **Bodies** — at most `MaxBodyBytes` bytes of each body are read and masked (default 4096; 0 turns body logging off). `RequestBodyStatus` and `ResponseBodyStatus` say what was logged: `Masked`, `Truncated`, `Invalid`, `Unrecognized`, `Incomplete`, `Withheld`, `Skipped`, `NotBuffered`, `Raw` or `Failed`. When no JSON could be written, the body is a marker such as `[body not JSON]` or `[body not logged: text/plain]`.
- **Skipped bodies** — a non-JSON media type, a charset other than UTF-8, or a `Content-Encoding` such as gzip. A request `StreamContent` whose stream cannot be read twice is logged as `[body not buffered]`.
- **The caller is unaffected** — the response is returned as soon as its headers arrive, so `HttpCompletionOption.ResponseHeadersRead` keeps streaming; the body stays readable in full; the original exception of a failed call is rethrown with its stack trace; and a failure inside logging (a throwing sink, masker provider or logger) is reported through the handler's logger and never reaches the caller.
- **When the entry is written** — the response body is captured in the background, so the entry is written once `MaxBodyBytes` bytes were captured, the body ended, the read failed or the caller disposed the response.
- **Never logged** — the query string: `Path` holds the path only.
- **`IncludeSensitive`** — logs bodies unmasked, marks the entry `BodyUnmasked` and logs a warning. Use it for local debugging only.
- **Options** are named after the client and read on every call, so a reload applies to the next call. `services.ConfigureAll<JsonBodyLoggingOptions>(...)` changes every client. Calling `AddJsonBodyLogging` twice for one client adds the handler once.

## Links

- Source and full documentation: https://github.com/DragoAnt/Extensions.System.Text.Json
- Release notes: https://github.com/DragoAnt/Extensions.System.Text.Json/releases
