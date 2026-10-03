---
name: json-observer-http-logging
description: Log HttpClient request and response JSON bodies with secrets masked, using DragoAnt.System.Text.Json.Observer.Http — AddJsonBodyLogging on IHttpClientBuilder, JsonBodyLoggingHandler (a DelegatingHandler), per-request model types with WithBodyLogging<TRequest,TResponse> or SendWithBodyLoggingAsync, When (Never, OnFailure, Always), MaxBodyBytes, IncludeSensitive, IJsonBodyMaskerProvider to pick a JsonObserver per body model, IJsonBodyLogSink for custom sinks, JsonBodyLogEntry fields, JsonBodyStatus and JsonBodyOutcome (Success, Failure, Exception, Canceled). Use when adding body logging to a named or typed HttpClient, deciding what is logged on failure, wiring maskers per request and response model, sending entries to your own sink, or debugging "[body withheld]", "[body not buffered]", "[body not logged: …]", "[body not JSON]", every logged value being "***", or a log entry that appears late or not at all.
---

# Logging HttpClient JSON bodies with DragoAnt.System.Text.Json.Observer.Http

`JsonBodyLoggingHandler` sits in an `HttpClient` pipeline and turns a call into one `JsonBodyLogEntry` — method, path, status, outcome, elapsed time, and the request and response bodies **masked** by a `JsonObserver` you choose per body model type. The caller is never affected: the response streams as usual, its body stays readable in full, and a failure inside logging never reaches the caller.

Package: `DragoAnt.System.Text.Json.Observer.Http` (net8.0, net9.0, net10.0), namespace `DragoAnt.System.Text.Json.Observer.Http`. Masking rules come from `DragoAnt.System.Text.Json.Observer` — see the `json-observer-masking` skill.

## Quick start

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;
using DragoAnt.System.Text.Json.Observer.Strategies;
using Microsoft.Extensions.DependencyInjection;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var sink = new ConsoleSink();
var services = new ServiceCollection();
services.AddSingleton<IJsonBodyMaskerProvider, PaymentMaskers>();
services.AddSingleton<IJsonBodyLogSink>(sink);
services.AddHttpClient("payments", client => client.BaseAddress = new Uri("https://api.example.com/"))
    .AddJsonBodyLogging(options => options.When = JsonBodyLogWhen.OnFailure)
    .ConfigurePrimaryHttpMessageHandler(() => new DeclineEverything());

await using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("payments");

using var request = new HttpRequestMessage(HttpMethod.Post, "charges?apiKey=k")
{
    Content = JsonContent.Create(new ChargeRequest("4111111111111111", 10.5m)),
}.WithBodyLogging<ChargeRequest, ChargeResponse>("Charge");
using var response = await client.SendAsync(request);

await sink.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));
// Output:
// Charge POST /charges 402 Failure
// request (Masked): {"cardNumber":"***1111","amount":10.5}
// response (Masked): {"id":"ch_1","error":"card_declined","cardNumber":"***1111"}

public sealed record ChargeRequest(string CardNumber, decimal Amount);

public sealed record ChargeResponse(string Id, string? Error);

public sealed class PaymentMaskers : IJsonBodyMaskerProvider
{
    private static readonly JsonObserver Charge = JsonObserver.Obj(Relative(rules => rules
            .Match("cardNumber").MaskAny(MaskTag.Last4),
        BlockList));

    public JsonObserver? GetMasker(Type? modelType, string clientName) =>
        modelType == typeof(ChargeRequest) || modelType == typeof(ChargeResponse) ? Charge : null;
}

public sealed class ConsoleSink : IJsonBodyLogSink
{
    public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Write(in JsonBodyLogEntry entry)
    {
        Console.WriteLine($"{entry.Operation} {entry.Method} {entry.Path} {entry.StatusCode} {entry.Outcome}");
        Console.WriteLine($"request ({entry.RequestBodyStatus}): {entry.RequestBody}");
        Console.WriteLine($"response ({entry.ResponseBodyStatus}): {entry.ResponseBody}");
        Written.TrySetResult();
    }
}

public sealed class DeclineEverything : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.PaymentRequired)
        {
            Content = new StringContent("""{"id":"ch_1","error":"card_declined","cardNumber":"4111111111111111"}""", Encoding.UTF8, "application/json"),
        });
}
```

In an application you register only the provider (and optionally a sink); the default sink writes to `ILogger`. The fake primary handler and the console sink above only make the example self-contained.

## Decision path

1. **Register** with `services.AddHttpClient(name).AddJsonBodyLogging(o => …)` — never `AddHttpMessageHandler<JsonBodyLoggingHandler>()`. Building a pipeline by hand: `new JsonBodyLoggingHandler(options, provider, sink, logger) { InnerHandler = … }` → [examples.md#manual-handler-chain](./examples.md#manual-handler-chain).
2. **Choose what is logged** with `When`: `OnFailure` (default — a non-2xx status, an exception, or a cancellation), `Always`, or `Never`.
3. **Register one `IJsonBodyMaskerProvider`** that maps body model types to observers built once. Without a provider every value is masked (`"***"`); a provider returning `null` withholds the body → [recipes.md#a-masker-provider-built-from-your-models](./recipes.md#a-masker-provider-built-from-your-models).
4. **Tell the handler the model types** per request: `request.WithBodyLogging<TRequest, TResponse>("Operation")` or `client.SendWithBodyLoggingAsync<TRequest, TResponse>(request, "Operation")`; or set `DefaultRequestType` / `DefaultResponseType` for a client that always sends one model.
5. **Need entries elsewhere** (metrics, a structured store, a test)? Register an `IJsonBodyLogSink` → [recipes.md#a-custom-sink](./recipes.md#a-custom-sink).

## Rules that matter

1. **Options are named per client** and read on every call; `services.ConfigureAll<JsonBodyLoggingOptions>(…)` changes every client. Calling `AddJsonBodyLogging` twice for one client adds the handler once.
2. **Defaults:** `When = OnFailure`, `MaxBodyBytes = 4096` (0 turns bodies off, calls are still logged), `IncludeSensitive = false`.
3. **Cache observers in the provider.** `GetMasker` runs for every logged body, possibly concurrently; return `static readonly` observers, never build one inside it.
4. **The response entry is written later**, on a background read, once `MaxBodyBytes` were captured, the body ended, the read failed, or the caller disposed the response. A sink must be thread-safe and must not assume it runs before `SendAsync` returns.
5. **A body that cannot be logged as JSON becomes a marker** and `RequestBodyStatus`/`ResponseBodyStatus` says why: `[body not logged: text/plain]` (`Skipped`), `[body not buffered]` (`NotBuffered`), `[body withheld]` (`Withheld`), `[body not JSON]` (`NotJson`), `[invalid JSON]` (`Invalid`), `[body incomplete]` (`Incomplete`), `[body not logged: masking failed]` (`Failed`).
6. **Cancellation and `HttpClient.Timeout` are both `Canceled`**; the handler cannot tell them apart. The original exception is rethrown unchanged.
7. **The query string is never logged** — `Path` is the path only.
8. **`IncludeSensitive = true` logs bodies unmasked** (`BodyUnmasked = true`, `Raw` status, a warning in the log). Local debugging only.

## Pitfalls (details in [pitfalls.md](./pitfalls.md))

- Every logged value is `"***"` → no `IJsonBodyMaskerProvider` is registered, or its observer uses the default `AllowList`.
- `[body withheld]` → the provider returned `null`: the request has no `WithBodyLogging<,>` and no `Default*Type`, so `modelType` is `null`.
- Nothing is logged for a successful call → `When` is `OnFailure` (the default).
- A test reads the sink right after `SendAsync` and finds nothing → the response entry is written asynchronously; wait for it.

## Companions

- [examples.md](./examples.md) — DI registration, a typed client, a manual handler chain, options, the entry fields.
- [recipes.md](./recipes.md) — a provider from your models, a custom sink, logging only some clients, large and streaming bodies.
- [pitfalls.md](./pitfalls.md) — the markers and surprises, with their causes.

Every C# block in these files is a complete program (top-level statements, implicit usings) that compiles against the library and prints what its `// Output:` comment shows.
