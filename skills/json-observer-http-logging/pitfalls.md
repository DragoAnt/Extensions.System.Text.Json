# Pitfalls — json-observer-http-logging

Symptom first, then the cause and the fix. The programs share one shape: a handler with fixed options, a capturing sink, and a fake inner handler.

## Every logged value is `"***"`

**Cause:** no `IJsonBodyMaskerProvider` is registered — the handler then masks every value of an object or array body — or the provider's observer uses the default `AllowList` (`JsonObserver.Obj(rules)` without a second argument).

**Fix:** register a provider; build its observers with `BlockList` (mask what you name) or from a shape (allow-list with known fields visible).

## `[body withheld]`

**Cause:** the provider returned `null`. Usually `modelType` is `null` because the request carries no `WithBodyLogging<TRequest, TResponse>()` and the options set no `DefaultRequestType` / `DefaultResponseType`.

**Fix:** attach the model types per request, set the defaults for a single-model client, or return a fallback observer for `null`.

```csharp
using System.Net;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

foreach (var attachTypes in new[] { false, true })
{
    var sink = new CaptureSink();
    var handler = new JsonBodyLoggingHandler(new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always }, new LoginMaskers(), sink)
    {
        InnerHandler = new Ok(),
    };
    using var client = new HttpClient(handler);
    using var request = new HttpRequestMessage(HttpMethod.Post, "https://auth.example.com/login")
    {
        Content = new StringContent("""{"user":"bob","password":"p"}""", Encoding.UTF8, "application/json"),
    };
    if (attachTypes)
    {
        request.WithBodyLogging<Login, object>("Login");
    }

    using var response = await client.SendAsync(request);
    var entry = await sink.Entry.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine($"{entry.RequestBodyStatus} {entry.RequestBody}");
}
// Output:
// Withheld [body withheld]
// Masked {"user":"bob","password":"***"}

public sealed record Login(string User, string Password);

public sealed class LoginMaskers : IJsonBodyMaskerProvider
{
    private static readonly JsonObserver Login = JsonObserver.Obj(Relative(rules => rules.Match("password").MaskAny("***"), BlockList));

    public JsonObserver? GetMasker(Type? modelType, string clientName) => modelType == typeof(Login) ? Login : null;
}

public sealed class CaptureSink : IJsonBodyLogSink
{
    public TaskCompletionSource<JsonBodyLogEntry> Entry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Write(in JsonBodyLogEntry entry) => Entry.TrySetResult(entry);
}

public sealed class Ok : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
}
```

## `[body not logged: …]`, `[body not buffered]`, `[body not JSON]`

| Marker | Status | Cause |
| --- | --- | --- |
| `[body not logged: text/plain]` | `Skipped` | the content type is not `application/json`, `text/json` or `*+json` (a missing content type is treated as JSON) |
| `[body not logged: charset utf-16]` | `Skipped` | a charset other than UTF-8 / ASCII |
| `[body not logged: encoding gzip]` | `Skipped` | a `Content-Encoding` other than `identity` |
| `[body not logged: MaxBodyBytes is 0]` | `Skipped` | bodies are switched off |
| `[body not buffered]` | `NotBuffered` | a request `StreamContent` whose stream cannot seek — reading it for the log would consume it |
| `[body not JSON]` | `NotJson` | a JSON body whose root is not an object or array, or an empty one |
| `[invalid JSON]` | `Invalid` | the body is not JSON and nothing could be read before the error |
| `[body incomplete]` | `Incomplete` | the response read failed, or the caller disposed the response before any body arrived |
| `[body not logged: masking failed]` | `Failed` | the provider threw |

```csharp
using System.Net;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;

var sink = new CaptureSink();
var handler = new JsonBodyLoggingHandler(new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always }, null, sink)
{
    InnerHandler = new PlainText(),
};
using var client = new HttpClient(handler);

using var upload = new StreamContent(new ForwardOnlyStream(Encoding.UTF8.GetBytes("""{"file":"data"}""")));
upload.Headers.ContentType = new("application/json");
using var response = await client.PostAsync("https://files.example.com/upload", upload);

var entry = await sink.Entry.Task.WaitAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"{entry.RequestBodyStatus} {entry.RequestBody}");
Console.WriteLine($"{entry.ResponseBodyStatus} {entry.ResponseBody}");
// Output:
// NotBuffered [body not buffered]
// Skipped [body not logged: text/plain]

public sealed class CaptureSink : IJsonBodyLogSink
{
    public TaskCompletionSource<JsonBodyLogEntry> Entry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Write(in JsonBodyLogEntry entry) => Entry.TrySetResult(entry);
}

public sealed class PlainText : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await request.Content!.CopyToAsync(Stream.Null, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("stored", Encoding.UTF8, "text/plain") };
    }
}

public sealed class ForwardOnlyStream(byte[] data) : MemoryStream(data)
{
    public override bool CanSeek => false;
}
```

## Nothing is logged for successful calls

**Cause:** `When` defaults to `OnFailure`. **Fix:** set `When = JsonBodyLogWhen.Always` for the clients you want fully logged.

## The log entry appears after `SendAsync` returned, or a test finds no entry

**Cause:** the response body is read for logging in the background; its entry is written once `MaxBodyBytes` were captured, the body ended, the read failed, or the caller disposed the response. An entry for a call **without** a response (exception, cancellation) is written before the exception reaches the caller.

**Fix:** in tests, wait on the sink with a timeout (a `TaskCompletionSource` or `SemaphoreSlim` released in `Write`) instead of reading it right after `SendAsync`. In production, make the sink thread-safe and do not depend on ordering with the caller.

## The response body looks consumed after adding logging

It is not: the handler replaces `response.Content` with a stream that replays the captured prefix and then continues from the network. Read the content you get **from the response after `SendAsync`**, not a reference to the original content taken inside a lower handler.

## A timeout is logged as `Canceled`

`HttpClient.Timeout` and a canceled caller token reach the handler as the same token, so both are `JsonBodyOutcome.Canceled`. Tell them apart at the call site (the caller knows whether it canceled).

## `IncludeSensitive` in production

`IncludeSensitive = true` writes bodies **unmasked** (`Raw`, `BodyUnmasked = true`) and logs a warning the first time. Keep it to local debugging; gate it on the environment if it is configurable at all.

## Registering the handler with `AddHttpMessageHandler<JsonBodyLoggingHandler>()`

The handler needs the client name and named options; `AddJsonBodyLogging()` supplies them. Registering the type directly loses both. Call `AddJsonBodyLogging` once per client (a second call only applies its `configure` action).

## Building observers inside `GetMasker`

`GetMasker` runs for every logged body, possibly concurrently. Building an observer there costs far more than masking with one; build them once (`static readonly` or in the provider's constructor) and return the cached instance.
