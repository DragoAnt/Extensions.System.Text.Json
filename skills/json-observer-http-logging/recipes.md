# Recipes — json-observer-http-logging

Task-shaped solutions. Each block is a complete program with a fake primary handler; the `// Output:` comment is what it prints.

## A masker provider built from your models

Build one observer per body model **once**, from the model's System.Text.Json metadata, and look it up by type. `JsonShape.FromTypeInfo` + `JsonObserver.FromShape` is an allow-list: known fields are logged, the fields you classify are masked, and a field the remote side adds later is masked too. Unknown model types return `null` → `[body withheld]`.

```csharp
using System.Collections.Frozen;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;
using DragoAnt.Observer;

var sink = new CaptureSink();
var handler = new JsonBodyLoggingHandler(new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always }, new ModelMaskers(), sink)
{
    InnerHandler = new FakeApi(),
};
using var client = new HttpClient(handler) { BaseAddress = new Uri("https://shop.example.com/") };

using var request = new HttpRequestMessage(HttpMethod.Post, "orders")
{
    Content = JsonContent.Create(new PlaceOrder("A1", 2, "4111111111111111")),
}.WithBodyLogging<PlaceOrder, OrderPlaced>("PlaceOrder");
using var response = await client.SendAsync(request);

var entry = await sink.Entry.Task.WaitAsync(TimeSpan.FromSeconds(5));
Console.WriteLine(entry.RequestBody);
Console.WriteLine(entry.ResponseBody);
// Output:
// {"sku":"A1","quantity":2,"cardNumber":"***1111"}
// {"orderId":"o-7","status":"accepted","fraudScore":"***"}

public sealed record PlaceOrder(string Sku, int Quantity, string CardNumber);

public sealed record OrderPlaced(string OrderId, string Status);

public sealed class ModelMaskers : IJsonBodyMaskerProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    private static readonly FrozenDictionary<Type, JsonObserver> Maskers = new Dictionary<Type, JsonObserver>
    {
        [typeof(PlaceOrder)] = For<PlaceOrder>(),
        [typeof(OrderPlaced)] = For<OrderPlaced>(),
    }.ToFrozenDictionary();

    public JsonObserver? GetMasker(Type? modelType, string clientName) =>
        modelType is not null && Maskers.TryGetValue(modelType, out var masker) ? masker : null;

    private static JsonObserver For<T>() =>
        JsonObserver.FromShape(JsonShape.FromTypeInfo(Json.GetTypeInfo(typeof(T)), Classify));

    private static MaskTag? Classify(JsonPropertyInfo property) => property.Name switch
    {
        "cardNumber" => MaskTag.Last4,
        "password" or "token" => MaskTag.Full,
        _ => null,
    };
}

public sealed class CaptureSink : IJsonBodyLogSink
{
    public TaskCompletionSource<JsonBodyLogEntry> Entry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Write(in JsonBodyLogEntry entry) => Entry.TrySetResult(entry);
}

public sealed class FakeApi : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"orderId":"o-7","status":"accepted","fraudScore":0.93}""", Encoding.UTF8, "application/json"),
        });
}
```

`fraudScore` is not in `OrderPlaced`, so the shape masks it. Use the same `JsonSerializerOptions` the client serializes with, so property names match what goes over the wire.

## A custom sink

Register an `IJsonBodyLogSink` to send entries somewhere other than `ILogger` — a metrics counter, an audit store, a test. `Write` may run concurrently and, for a response, on a background thread after `SendAsync` returned; keep it fast and thread-safe, and never throw for control flow (an exception is reported through the handler's logger, never to the caller).

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using DragoAnt.System.Text.Json.Observer.Http;
using Microsoft.Extensions.DependencyInjection;

var sink = new JsonLinesSink();
var services = new ServiceCollection();
services.AddSingleton<IJsonBodyLogSink>(sink);
services.AddHttpClient("inventory").AddJsonBodyLogging().ConfigurePrimaryHttpMessageHandler(() => new NotFound());

await using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("inventory");
using var response = await client.GetAsync("https://inventory.example.com/items/9?sig=secret");

Console.WriteLine(await sink.NextLineAsync());
// Output:
// {"client":"inventory","method":"GET","path":"/items/9","status":404,"outcome":"Failure","response":"{\u0022error\u0022:\u0022***\u0022}"}

public sealed class JsonLinesSink : IJsonBodyLogSink
{
    private readonly BlockingCollection<string> _lines = new();

    public void Write(in JsonBodyLogEntry entry) => _lines.Add(JsonSerializer.Serialize(new
    {
        client = entry.ClientName,
        method = entry.Method.Method,
        path = entry.Path,
        status = entry.StatusCode,
        outcome = entry.Outcome.ToString(),
        response = entry.ResponseBody,
    }));

    public Task<string> NextLineAsync() => Task.Run(() => _lines.Take()).WaitAsync(TimeSpan.FromSeconds(5));
}

public sealed class NotFound : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error":"item 9 not found"}""", Encoding.UTF8, "application/json"),
        });
}
```

No provider is registered here, so every value is masked; the query string (`sig=secret`) never reaches the entry.

## The default sink and its log message

Without a registered sink, entries go to `ILogger<JsonBodyLoggingHandler>` as one structured message, `Information` for `Success` and `Warning` otherwise. Every entry field except `Exception` is a template property (`ClientName`, `Operation`, `Method`, `Path`, `StatusCode`, `ElapsedMs`, `Outcome`, `Truncated`, `BodyUnmasked`, `RequestBodyStatus`, `RequestBody`, `ResponseBodyStatus`, `ResponseBody`), so a structured logger indexes them; the exception is attached to the message.

```csharp
using System.Net;
using System.Text;
using DragoAnt.System.Text.Json.Observer.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var logs = new CapturingProvider();
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddProvider(logs));
services.AddHttpClient("weather").AddJsonBodyLogging().ConfigurePrimaryHttpMessageHandler(() => new Unavailable());

await using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("weather");
using var response = await client.GetAsync("https://weather.example.com/today");

var (level, properties) = await logs.First.Task.WaitAsync(TimeSpan.FromSeconds(5));
Console.WriteLine(level);
Console.WriteLine($"{properties["ClientName"]} {properties["StatusCode"]} {properties["Outcome"]} {properties["ResponseBody"]}");
// Output:
// Warning
// weather 503 Failure {"retryAfter":"***"}

public sealed class CapturingProvider : ILoggerProvider
{
    public TaskCompletionSource<(LogLevel, Dictionary<string, object?>)> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs && pairs.Any(p => p.Key == "ResponseBody"))
            {
                owner.First.TrySetResult((logLevel, pairs.ToDictionary(p => p.Key, p => p.Value)));
            }
        }
    }
}

public sealed class Unavailable : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"retryAfter":30}""", Encoding.UTF8, "application/json"),
        });
}
```

## Log every call for one client, failures for the rest

Options are named after the client. Configure the exception client in its own `AddJsonBodyLogging`, and shared settings with `ConfigureAll` (applied first, so the per-client action wins).

```csharp
using DragoAnt.System.Text.Json.Observer.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.ConfigureAll<JsonBodyLoggingOptions>(options =>
{
    options.When = JsonBodyLogWhen.OnFailure;
    options.MaxBodyBytes = 8 * 1024;
});
services.AddHttpClient("payments").AddJsonBodyLogging(options => options.When = JsonBodyLogWhen.Always);
services.AddHttpClient("catalog").AddJsonBodyLogging();
services.AddHttpClient("health").AddJsonBodyLogging(options => options.When = JsonBodyLogWhen.Never);

await using var provider = services.BuildServiceProvider();
var monitor = provider.GetRequiredService<IOptionsMonitor<JsonBodyLoggingOptions>>();
Console.WriteLine(string.Join(", ", new[] { "payments", "catalog", "health" }.Select(n => $"{n}={monitor.Get(n).When}")));
// Output:
// payments=Always, catalog=OnFailure, health=Never
```

## Large and streaming responses

Only the first `MaxBodyBytes` of a body are read for the log; the caller still receives the whole body, and `HttpCompletionOption.ResponseHeadersRead` still returns as soon as the headers arrive. A longer body is logged as its masked prefix, closed into valid JSON, with `ResponseBodyStatus = Truncated` and `Truncated = true`.

```csharp
using System.Net;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Http;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var sink = new CaptureSink();
var options = new JsonBodyLoggingOptions { When = JsonBodyLogWhen.Always, MaxBodyBytes = 40 };
var handler = new JsonBodyLoggingHandler(options, new KeepAll(), sink) { InnerHandler = new BigList() };
using var client = new HttpClient(handler);

using var response = await client.GetAsync("https://api.example.com/items", HttpCompletionOption.ResponseHeadersRead);
var body = await response.Content.ReadAsStringAsync();
var entry = await sink.Entry.Task.WaitAsync(TimeSpan.FromSeconds(5));

Console.WriteLine($"caller read {body.Length} chars");
Console.WriteLine($"{entry.ResponseBodyStatus} {entry.Truncated} {entry.ResponseBody}");
// Output:
// caller read 1001 chars
// Truncated True {"items":[{"id":0},{"id":1},{"id":2}]}

public sealed class KeepAll : IJsonBodyMaskerProvider
{
    private static readonly JsonObserver Masker = JsonObserver.Obj(BlockList);

    public JsonObserver? GetMasker(Type? modelType, string clientName) => Masker;
}

public sealed class CaptureSink : IJsonBodyLogSink
{
    public TaskCompletionSource<JsonBodyLogEntry> Entry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Write(in JsonBodyLogEntry entry) => Entry.TrySetResult(entry);
}

public sealed class BigList : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var items = string.Join(",", Enumerable.Range(0, 100).Select(i => $$"""{"id":{{i}}}"""));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"items":[{{items}}]}""", Encoding.UTF8, "application/json"),
        });
    }
}
```
