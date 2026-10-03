# Examples — json-observer-http-logging

Each block is a complete program: a console project referencing `DragoAnt.System.Text.Json.Observer.Http` (it brings `Microsoft.Extensions.Http` and the core package). A fake primary handler stands in for the network so the programs run offline; the `// Output:` comment is what they print.

## Typed client with DI

`AddJsonBodyLogging` works on any `IHttpClientBuilder`, so typed clients get it too. The handler resolves `IJsonBodyMaskerProvider`, `IJsonBodyLogSink` and `ILogger<JsonBodyLoggingHandler>` from DI when they are registered. `SendWithBodyLoggingAsync<TRequest, TResponse>` attaches the model types and sends in one call.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;
using Microsoft.Extensions.DependencyInjection;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var sink = new CaptureSink();
var services = new ServiceCollection();
services.AddSingleton<IJsonBodyMaskerProvider, UserMaskers>();
services.AddSingleton<IJsonBodyLogSink>(sink);
services.AddHttpClient<UsersClient>(client => client.BaseAddress = new Uri("https://users.example.com/"))
    .AddJsonBodyLogging(options => options.When = JsonBodyLogWhen.Always)
    .ConfigurePrimaryHttpMessageHandler(() => new FakeApi());

await using var provider = services.BuildServiceProvider();
var users = provider.GetRequiredService<UsersClient>();
Console.WriteLine(await users.CreateAsync(new CreateUser("alice", "s3cret")));

var entry = await sink.Entry.Task.WaitAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"{entry.ClientName} {entry.Operation} {entry.StatusCode} {entry.Outcome} request={entry.RequestBody} response={entry.ResponseBody}");
// Output:
// created 42
// UsersClient CreateUser 201 Success request={"login":"alice","password":"***"} response={"id":42,"apiToken":"***"}

public sealed record CreateUser(string Login, string Password);

public sealed record CreatedUser(int Id, string ApiToken);

public sealed class UsersClient(HttpClient http)
{
    public async Task<string> CreateAsync(CreateUser user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "users") { Content = JsonContent.Create(user) };
        using var response = await http.SendWithBodyLoggingAsync<CreateUser, CreatedUser>(request, "CreateUser");
        var created = await response.Content.ReadFromJsonAsync<CreatedUser>();
        return $"created {created!.Id}";
    }
}

public sealed class UserMaskers : IJsonBodyMaskerProvider
{
    private static readonly JsonObserver Users = JsonObserver.Obj(Relative(rules => rules
            .Match("password").MaskAny("***")
            .Match("apiToken").MaskAny("***"),
        BlockList));

    public JsonObserver? GetMasker(Type? modelType, string clientName) =>
        modelType == typeof(CreateUser) || modelType == typeof(CreatedUser) ? Users : null;
}

public sealed class CaptureSink : IJsonBodyLogSink
{
    public TaskCompletionSource<JsonBodyLogEntry> Entry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Write(in JsonBodyLogEntry entry) => Entry.TrySetResult(entry);
}

public sealed class FakeApi : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":42,"apiToken":"tok_live_123"}""", Encoding.UTF8, "application/json"),
        });
}
```

A typed client's name is the type name (`UsersClient`); that is the name its options and `entry.ClientName` use.

## Manual handler chain

Without DI, construct the handler with fixed options and set its `InnerHandler`. The client name is empty in this form.

```csharp
using System.Net;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var sink = new ListSink();
var options = new JsonBodyLoggingOptions
{
    When = JsonBodyLogWhen.Always,
    MaxBodyBytes = 1024,
    DefaultRequestType = typeof(object),
    DefaultResponseType = typeof(object),
};
var handler = new JsonBodyLoggingHandler(options, new OneMasker(), sink) { InnerHandler = new Echo() };
using var client = new HttpClient(handler) { BaseAddress = new Uri("https://echo.example.com/") };

using var response = await client.PostAsync("echo", new StringContent("""{"user":"bob","token":"t-1"}""", Encoding.UTF8, "application/json"));
Console.WriteLine(await response.Content.ReadAsStringAsync());

var entry = await sink.WaitAsync();
Console.WriteLine($"[{entry.ClientName}] {entry.Method} {entry.Path} {entry.RequestBody} -> {entry.ResponseBody}");
// Output:
// {"user":"bob","token":"t-1"}
// [] POST /echo {"user":"bob","token":"***"} -> {"user":"bob","token":"***"}

public sealed class OneMasker : IJsonBodyMaskerProvider
{
    private static readonly JsonObserver Masker = JsonObserver.Obj(Relative(rules => rules.Match("token").MaskAny("***"), BlockList));

    public JsonObserver? GetMasker(Type? modelType, string clientName) => Masker;
}

public sealed class ListSink : IJsonBodyLogSink
{
    private readonly TaskCompletionSource<JsonBodyLogEntry> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Write(in JsonBodyLogEntry entry) => _first.TrySetResult(entry);

    public Task<JsonBodyLogEntry> WaitAsync() => _first.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

public sealed class Echo : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(await request.Content!.ReadAsStringAsync(cancellationToken), Encoding.UTF8, "application/json"),
        };
}
```

The caller still reads the full, unmasked response: only the logged copy is masked.

## Options

| Option | Default | Effect |
| --- | --- | --- |
| `When` | `OnFailure` | `Never`, `OnFailure` (non-2xx, exception, cancellation) or `Always` |
| `MaxBodyBytes` | 4096 | bytes of each body read and masked; a longer body is logged truncated (`Truncated = true`); 0 or less logs calls without bodies |
| `IncludeSensitive` | `false` | log bodies unmasked; marks the entry `BodyUnmasked` and logs a warning once |
| `DefaultRequestType` / `DefaultResponseType` | `null` | model types passed to the provider when the request has no `WithBodyLogging` |

With DI they are named options per client name, read on every call, so an options reload applies to the next call:

```csharp
using DragoAnt.System.Text.Json.Observer.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.ConfigureAll<JsonBodyLoggingOptions>(options => options.MaxBodyBytes = 2048);
services.AddHttpClient("orders").AddJsonBodyLogging(options => options.When = JsonBodyLogWhen.Always);
services.AddHttpClient("search").AddJsonBodyLogging();

await using var provider = services.BuildServiceProvider();
var monitor = provider.GetRequiredService<IOptionsMonitor<JsonBodyLoggingOptions>>();
foreach (var name in new[] { "orders", "search" })
{
    var options = monitor.Get(name);
    Console.WriteLine($"{name}: {options.When} {options.MaxBodyBytes}");
}
// Output:
// orders: Always 2048
// search: OnFailure 2048
```

## The entry

| Field | Content |
| --- | --- |
| `ClientName` | client name; empty for a handler built without one |
| `Operation` | the name given to `WithBodyLogging` / `SendWithBodyLoggingAsync`, or `null` |
| `Method`, `Path` | HTTP method; request path without query string and fragment |
| `StatusCode` | response status, or `null` when no response arrived |
| `ElapsedMs` | time until the response headers arrived or the call failed |
| `Outcome` | `Success` (2xx), `Failure` (other status), `Exception`, `Canceled` |
| `RequestBody`, `ResponseBody` | masked JSON, a bracketed marker, or `null` when there is no body |
| `RequestBodyStatus`, `ResponseBodyStatus` | `None`, `Masked`, `Truncated`, `Invalid`, `NotJson`, `Raw`, `Withheld`, `Skipped`, `NotBuffered`, `Incomplete`, `Failed` |
| `Truncated` | a body was longer than `MaxBodyBytes` |
| `BodyUnmasked` | a body was logged with `IncludeSensitive` |
| `Exception` | the exception the call failed with; rethrown to the caller unchanged |

## Exceptions and cancellations

Under `OnFailure`, a call that throws is logged without a response, then the exception reaches the caller unchanged. A canceled token and `HttpClient.Timeout` both log `Canceled`.

```csharp
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var sink = new PrintSink();
var masker = new FixedMasker(JsonObserver.Obj(Relative(rules => rules.Match("pin").MaskAny("***"), BlockList)));
var handler = new JsonBodyLoggingHandler(new JsonBodyLoggingOptions(), masker, sink) { InnerHandler = new Unreachable() };
using var client = new HttpClient(handler) { BaseAddress = new Uri("https://bank.example.com/") };

try
{
    await client.PostAsync("pin", new StringContent("""{"pin":"1234"}""", Encoding.UTF8, "application/json"));
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"caller sees: {ex.Message}");
}

using var canceled = new CancellationTokenSource();
canceled.Cancel();
try
{
    await client.GetAsync("balance", canceled.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("caller sees: canceled");
}
// Output:
// Exception status= request={"pin":"***"} error=connection refused
// caller sees: connection refused
// Canceled status= request= error=The operation was canceled.
// caller sees: canceled

public sealed class FixedMasker(JsonObserver masker) : IJsonBodyMaskerProvider
{
    public JsonObserver? GetMasker(Type? modelType, string clientName) => masker;
}

public sealed class PrintSink : IJsonBodyLogSink
{
    public void Write(in JsonBodyLogEntry entry) =>
        Console.WriteLine($"{entry.Outcome} status={entry.StatusCode} request={entry.RequestBody} error={entry.Exception?.Message}");
}

public sealed class Unreachable : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new HttpRequestException("connection refused");
    }
}
```

An entry without a response is written synchronously, before the exception reaches the caller; an entry with a response is written when the background body read ends.
