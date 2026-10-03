# DragoAnt.System.Text.Json.Observer

Mask or extract JSON values by property-path rules in a single streaming pass from `Utf8JsonReader` to `Utf8JsonWriter` — no deserialization, no DOM.

[![Build](https://img.shields.io/github/actions/workflow/status/DragoAnt/Extensions.System.Text.Json/build.yml?branch=main)](https://github.com/DragoAnt/Extensions.System.Text.Json/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/DragoAnt.System.Text.Json.Observer)](https://www.nuget.org/packages/DragoAnt.System.Text.Json.Observer)
[![Downloads](https://img.shields.io/nuget/dt/DragoAnt.System.Text.Json.Observer)](https://www.nuget.org/packages/DragoAnt.System.Text.Json.Observer)
[![License](https://img.shields.io/github/license/DragoAnt/Extensions.System.Text.Json)](https://github.com/DragoAnt/Extensions.System.Text.Json/blob/main/LICENSE)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)

> ⚡ **Primary Goal:** Eliminate heap allocations, GC pauses, and Large Object Heap (LOH) pressure during high-throughput HTTP request and response body logging.
>
> 📊 **[Jump to Performance & Memory Benchmarks](#performance--memory-benchmarks)** | 🔍 **[Read the OSS Analogs Comparison](docs/comparisons/analogs.md)**

---

## Performance & Memory Benchmarks

Under high request traffic (e.g. 1,000+ req/sec in API gateways or payment webhooks), conventional DOM-based maskers parse and re-serialize the entire JSON tree into memory, allocating **3.5×–4.5× the body size** per call and triggering continuous Gen0/Gen1/Gen2 Garbage Collection.

`DragoAnt.System.Text.Json.Observer` executes a **single forward streaming pass** directly from `Utf8JsonReader` to `Utf8JsonWriter` over UTF-8 bytes:

### Head-to-Head Performance & Memory (.NET 10.0 x64 RyuJIT)

| Payload Size & Shape | DragoAnt Observer (bytes) | DOM `JsonNode` | [JsonMasking 2.0](https://github.com/ThiagoBarradas/jsonmasking) (~876k dl) | Speedup vs JsonMasking | Memory Reduction |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **1 KB Flat** | **0 B** / 8.3 µs | 11,000 B / 8.0 µs | 57,633 B / 98.0 µs | **11.8× faster** | **17× less RAM** |
| **8 KB Flat** | **0 B** / 70.8 µs | 86,865 B / 61.8 µs | 468,078 B / 902.0 µs | **12.8× faster** | **703× less RAM** |
| **64 KB Flat** | **0 B** / 534.0 µs | 709,628 B (LOH!) | 3,594,477 B (LOH!) / 8,173.0 µs | **15.3× faster** | **5,364× less RAM** |
| **64 KB Array** | **60 KB** / 639.6 µs | 923,927 B (LOH!) | 3,904,063 B (LOH!) / 7,538.2 µs | **11.8× faster** | **64× less RAM** |

### Allocated Bytes per Call (Noise-free, lower is better)

| Payload Size & Shape | LowerBound (bytes copy) | **DragoAnt Observer 2.0 (bytes)** | LowerBound (string copy) | DragoAnt Observer 2.0 (string) | DOM `JsonNode` / `JsonMasking 2.0` |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **1 KB Flat** | 0 B | **0 B** | 3 344 B | 3 410 B | 11 000 B *(3.3×)* |
| **8 KB Flat** | 0 B | **0 B** | 24 872 B | 25 100 B | 86 865 B *(3.5×)* |
| **8 KB Nested** | 0 B | **0 B** | 25 832 B | 26 050 B | 99 129 B *(3.8×)* |
| **8 KB Array (orders)** | 0 B | **0 B** | 24 416 B | 24 720 B | 112 953 B *(4.6×)* |
| **64 KB Flat** | 0 B | **0 B** | 197 158 B | 197 500 B | **709 609 B (LOH!)** |
| **64 KB Array (~100 items)**| 0 B | **0 B** | 200 562 B | 201 100 B | **923 894 B (LOH!)** |

*Measured with BenchmarkDotNet on .NET 9.0 and .NET 10.0 (x64 RyuJIT). Detailed BenchmarkDotNet logs: [benchmarks.md](docs/benchmarks/benchmarks.md).*

### Why Observer 2.0 Outperforms Analogs

* **0 B Heap Allocations on Byte Streams:** When called with `ReadOnlySpan<byte>` and a pooled `IBufferWriter<byte>`, zero objects are allocated on the managed heap.
* **Pre-encoded UTF-8 Property Matching:** Property names are matched directly on their raw UTF-8 bytes without materializing intermediate string objects.
* **Fail-Closed Truncation Safety & Container Synthesis:** When JSON is incomplete or cut short mid-body (e.g. `{"user":{"DriverLicense":"vvvvvvv3444"`), Observer masks all sensitive tokens up to the cutoff, automatically synthesizes missing closing braces (`}}`), outputs syntactically valid JSON, and returns `MaskStatus.Truncated`. Analogs either throw unhandled exceptions ([JsonMasking](https://github.com/ThiagoBarradas/jsonmasking)) or silently leak raw unmasked data ([Slin.Masking](https://github.com/sw0/Slin.Masking)).

| Truncated JSON Handling | Behavior | Security / Stability |
| :--- | :--- | :--- |
| **[DragoAnt Observer](https://github.com/DragoAnt/Extensions.System.Text.Json)** | **Synthesizes missing closing braces (`}}`)** | ✅ **100% safe.** Valid JSON emitted, 0 leaks, 0 crashes. |
| **[JsonMasking](https://github.com/ThiagoBarradas/jsonmasking)** | Throws `JsonReaderException: '}' expected` | ❌ Crash or raw body leak on fallback. |
| **[Slin.Masking](https://github.com/sw0/Slin.Masking)** | Returns raw string unmasked | 🚨 **Severe security leak** in logs/SIEM. |

👉 For an in-depth architectural comparison against `JsonMasking`, `Slin.Masking`, and `Microsoft.Extensions.Compliance.Redaction`, see the **[OSS Analogs Comparison](docs/comparisons/analogs.md)**.

---

## Features

- **Zero-allocation streaming byte API:** `Mask(ReadOnlySpan<byte>, IBufferWriter<byte>)` executes with **0 B heap allocations**.
- **Fail-closed security:** Never throws on truncated/malformed bodies; safely outputs already-masked tokens with `...[truncated]` marker.
- **HTTP client logging package:** `DragoAnt.System.Text.Json.Observer.Http` provides stream-preserving `JsonBodyLoggingHandler` for `HttpClient`.
- **Mask** sensitive values (cards, passwords, emails, tokens) with a constant, regex, rule tag (`MaskKind`), or custom delegate.
- **Type-agnostic masking:** Sensitive names mask strings, numbers, booleans, and whole objects/arrays (`MaskAny`).
- **Extract** values into a typed context object, or mask and extract in the same single forward pass.
- **Absolute rules** (`order` → `id`) and **relative rules** that match a path suffix at any depth (`*card` → `saved` → `id`).
- **Pre-encoded UTF-8 matching:** Property names match case-insensitively directly on UTF-8 bytes without string materialization.
- **Safe by default:** with no default policy, every string and number that no rule names is masked.
- Optional indented output, comment handling and dropping of `null` properties.

## Install

```sh
dotnet add package DragoAnt.System.Text.Json.Observer
```

## Quick start

```csharp
using System.Text.RegularExpressions;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var masking = JsonObserver.Obj(Relative(rules => rules
        .Match("card", "number").MaskStr(new Regex("(?<=.{6}).(?=.{4})"))
        .Match(PropMatches.Contains("password")).MaskStr("*****"),
    BlockList));

var json = """
    {"user":{"login":"alice","password":"s3cret"},"card":{"number":"4111111111111111","holder":"ALICE SMITH"}}
    """;

Console.WriteLine(masking.Mask(json));
```

Output:

```json
{"user":{"login":"alice","password":"*****"},"card":{"number":"411111******1111","holder":"ALICE SMITH"}}
```

Build an observer once (for example in a `static readonly` field) and reuse it for every payload.

## Usage

### Default policies

The last argument of `Relative(...)` (or of `JsonObserver.Obj(...)`) decides what happens to values no rule names. Booleans and `null` are always written unchanged.

| Policy | Unmatched strings and numbers |
| --- | --- |
| `AllowList` (default) | replaced with `#str#*****` / `#number#*****` |
| `BlockList` | written unchanged |
| `NullList` | written as `null` |

### Absolute rules

Absolute rules follow the path from the root. With the default `AllowList`, only the listed values stay readable:

```csharp
var masking = JsonObserver.Obj(root => root
    .Match("order").Obj(order => order
        .Match("id").Unmasked()
        .Match("status").Unmasked()));

masking.Mask("""{"order":{"id":42,"status":"paid","customer":"Alice Smith","paid":true},"note":"call me"}""");
// {"order":{"id":42,"status":"paid","customer":"#str#*****","paid":true},"note":"#str#*****"}
```

### Relative rules

Relative rules match the end of the path, wherever it occurs:

```csharp
var masking = JsonObserver.Obj(Relative(rules => rules
        .Match(PropMatches.EndsWith("card"), "saved", "id").MaskStr("***")
        .Match("email").MaskStr("***"),
    BlockList));

masking.Mask("""{"session":{"MY_card":{"saved":{"id":"c-9f2a"}},"customer":{"email":"alice@example.com","tier":"gold"}}}""");
// {"session":{"MY_card":{"saved":{"id":"***"}},"customer":{"email":"***","tier":"gold"}}}
```

### Custom strategy

`MaskStr` takes a regex (matches become `*`), a constant, or a function of the value and the context. `MaskInt`, `MaskLong`, `MaskDecimal`, `MaskBool` and `MaskRawValue` do the same for other token types.

```csharp
var masking = JsonObserver.Obj(Relative(rules => rules
        .Match("iban").MaskStr((value, _) => value is null ? null : new string('*', value.Length - 4) + value[^4..]),
    BlockList));

masking.Mask("""{"payout":{"iban":"DE89370400440532013000","currency":"EUR"}}""");
// {"payout":{"iban":"******************3000","currency":"EUR"}}
```

### Extracting values

`Read*` rules copy values into a context object; `Read` walks the JSON without writing anything:

```csharp
var observer = JsonObserver.Obj<RequestInfo>(root => root
        .Match("routing").Obj(routing => routing
            .Match("contractId").ReadInt((value, info) => info.ContractId = value)
            .Match("method").ReadStr((value, info) => info.Method = value)),
    JsonObserverValuePolicies<RequestInfo>.Relative(rules => rules
        .Match(PropMatches.Contains("ipAddress")).ReadStr((value, info) => info.Ip = value)));

var info = new RequestInfo();
observer.Read("""{"routing":{"contractId":2,"method":"card"},"session":{"browser":{"ipAddress":"203.0.113.7"}}}""", info);
// info: ContractId = 2, Method = "card", Ip = "203.0.113.7"

sealed class RequestInfo
{
    public int? ContractId { get; set; }
    public string? Method { get; set; }
    public string? Ip { get; set; }
}
```

Call `observer.Mask(json, info)` instead to mask and extract in one pass.

### Zero-allocation byte streaming (High-load hot path)

For maximum throughput and 0 B heap allocations, pass a `ReadOnlySpan<byte>` and write to a pooled or reusable `IBufferWriter<byte>`:

```csharp
using System.Buffers;
using DragoAnt.System.Text.Json.Observer;

var output = new ArrayBufferWriter<byte>(1024);
ReadOnlySpan<byte> utf8Json = """{"user":"alice","password":"secret"}"""u8;

MaskResult result = masking.Mask(utf8Json, output);
// result.Status == MaskStatus.Masked
// output.WrittenSpan contains the masked UTF-8 bytes: {"user":"alice","password":"***"}
```

If the JSON stream is cut short by a network timeout or HTTP size cap:
- `result.Status` will be `MaskStatus.Truncated` or `MaskStatus.Invalid`.
- `result.BytesWritten` gives the exact length of safely masked prefix emitted with `...[truncated]`.
- **Zero sensitive data is ever leaked.**

### HTTP Client Body Logging (`DragoAnt.System.Text.Json.Observer.Http`)

```sh
dotnet add package DragoAnt.System.Text.Json.Observer.Http
```

`JsonBodyLoggingHandler` logs masked JSON request and response bodies of an `HttpClient`. Register it per named client, and register an `IJsonBodyMaskerProvider` that picks the masker by body model type:

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
    private static readonly JsonObserver Charge = JsonObserver.Obj(rules => rules.Match("cardNumber").MaskStr("****"));

    // null logs the body as "[body withheld]".
    public JsonObserver? GetMasker(Type? modelType, string clientName) =>
        modelType == typeof(ChargeRequest) ? Charge : null;
}
```

Without an `IJsonBodyMaskerProvider` every value of an object or array body is masked. Each call becomes one `JsonBodyLogEntry`, written by an `IJsonBodyLogSink` — by default one structured `ILogger` message, `Information` for a success and `Warning` otherwise.

- **What is logged** — `When`: `Never`, `OnFailure` (default: a non-success status, an exception, or a cancellation) or `Always`. A cancellation and an `HttpClient.Timeout` reach the handler as one token, so both are logged with the outcome `Canceled`. The original exception is rethrown with its stack trace.
- **Bodies** — at most `MaxBodyBytes` (default 4096; 0 turns bodies off) of each body is read and masked. `RequestBodyStatus` / `ResponseBodyStatus` say what was logged: `Masked`, `Truncated`, `Invalid`, `NotJson`, `Incomplete`, `Withheld`, `Skipped` (non-JSON media type, a non-UTF-8 charset, a `Content-Encoding`), `NotBuffered` (a request stream that cannot be read twice), `Raw` or `Failed`. When no JSON could be written the body is a marker such as `[body not JSON]`.
- **The caller is unaffected** — the response is returned as soon as its headers arrive (`ResponseHeadersRead` keeps streaming), its body stays readable in full, and a failure inside logging is reported through the handler's logger instead of reaching the caller. The response body is captured in the background, so its entry is written when the captured part ends, the body ends, or the caller disposes the response.
- **Not logged** — the query string (`Path` is the path only). `IncludeSensitive = true` logs bodies unmasked, marks the entry `BodyUnmasked` and logs a warning; use it for local debugging only.
- **Options** are named per client and read on every call, so a reload applies to the next call; `services.ConfigureAll<JsonBodyLoggingOptions>(...)` changes every client. Add the handler with `AddJsonBodyLogging`, not `AddHttpMessageHandler<JsonBodyLoggingHandler>()`.

### Reader and writer options

`Mask` also takes `JsonReaderOptions` (for example `CommentHandling = JsonCommentHandling.Allow`), `JsonWriterOptions` (for example `Indented = true`), `ignoreNulls` and `ignoreComments`.

## Compatibility

Targets `net8.0`, `net9.0` and `net10.0`. No dependencies beyond the .NET base class library.

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](./CONTRIBUTING.md). Report security issues privately as described in [SECURITY.md](./SECURITY.md).

## License

[MIT](./LICENSE)

---

*Originally written by hand and empowered by AI.*
