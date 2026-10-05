# DragoAnt.System.Text.Json.Observer

Mask or extract JSON values by property-path rules in a single streaming pass from `Utf8JsonReader` to `Utf8JsonWriter` — no deserialization, no DOM.

[![CI](https://img.shields.io/github/actions/workflow/status/DragoAnt/Extensions.System.Text.Json/ci.yml?branch=main)](https://github.com/DragoAnt/Extensions.System.Text.Json/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DragoAnt.System.Text.Json.Observer)](https://www.nuget.org/packages/DragoAnt.System.Text.Json.Observer)
[![Downloads](https://img.shields.io/nuget/dt/DragoAnt.System.Text.Json.Observer)](https://www.nuget.org/packages/DragoAnt.System.Text.Json.Observer)
[![License](https://img.shields.io/github/license/DragoAnt/Extensions.System.Text.Json)](https://github.com/DragoAnt/Extensions.System.Text.Json/blob/main/LICENSE)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)

## In short

- **What:** you describe which properties are sensitive (`password`, `card.number`, anything ending in `token`); the observer rewrites the JSON with those values masked, and can hand selected values to a context object on the way. It reads and writes UTF-8 tokens directly, so a body is never turned into objects or a DOM.
- **Built for logging:** it never throws, even on cut-off or invalid JSON, and never writes a masked value in clear — a cut-off body gives its masked prefix, closed into valid JSON.
- **Install:** `dotnet add package DragoAnt.System.Text.Json.Observer` (`net8.0`, `net10.0`; it brings [DragoAnt.Observer.Core](https://www.nuget.org/packages/DragoAnt.Observer.Core), the format-neutral types, and imports the `DragoAnt.Observer` namespace into every C# file — set `<DragoAntObserverImplicitUsing>false</DragoAntObserverImplicitUsing>` to opt out). Builds with any .NET 8+ SDK.

### Quick start

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var masker = JsonObserver.Obj(AnyDepth(rules => rules
        .Match("password").Mask("***")
        .Path("card", "number").Mask(MaskTag.Last4),
    BlockList));

Console.WriteLine(masker.Mask("""{"user":"alice","password":"s3cret","card":{"number":"4111111111111111"}}"""));
// Output:
// {"user":"alice","password":"***","card":{"number":"***1111"}}
```

Build an observer once (a `static readonly` field) and share it: it is thread-safe.

### Mask or extract

**Mask** rules (`Mask`, `MaskInt`, …) replace a value. **Read** rules (`ReadStr`, `ReadInt`, …) hand a value to a context object; they do not decide what is written, so the default policy writes the value unless `.Unmasked()` or a mask method is chained on the read (`Match("ssn").ReadStr(f).Mask(MaskTag.Last4)`). One observer can do both in the same pass:

```csharp
using DragoAnt.System.Text.Json.Observer;

var observer = JsonObserver.Obj<Order>(
    rules => rules
        .Match("id").ReadInt((id, order) => order.Id = id)
        .Match("card").Mask("***"),
    ValuePolicy.BlockList);

var order = new Order();
Console.WriteLine(observer.Mask("""{"id":42,"card":"4111111111111111","total":9.5}""", order));
Console.WriteLine(order.Id);
// Output:
// {"id":42,"card":"***","total":9.5}
// 42

sealed class Order
{
    public int? Id { get; set; }
}
```

`observer.Read(json, order)` extracts without writing anything.

### Using an AI coding agent?

Ready-made agent skills for masking, HTTP body logging and testing — and how to install them in Claude Code, Codex, GitHub Copilot, Cursor, Gemini CLI or Antigravity — are in [Agent skills](./docs/skills.md).

---

## Rules

### Default policy — what happens to values no rule names

The last argument of a factory or of `AnyDepth(...)` decides: a `ValuePolicy` (it works for observers of any context type), an `AnyDepth(...)` rule set, or `JsonValuePolicy.Custom(rule)`. Booleans are values like any other.

| Policy | `{"s":"x","n":1,"b":true,"z":null}` becomes |
| --- | --- |
| `AllowList` (the default) | `{"s":"***","n":"***","b":"***","z":null}` |
| `BlockList` | unchanged: only values a rule names are masked |
| `NullList` | `{"s":null,"n":null,"b":null,"z":null}` |
| `Tagged(tag)` | every string, number and boolean masked by the call's strategy with the tag, for example hashed |

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

const string json = """{"s":"x","n":1,"b":true,"z":null}""";
Console.WriteLine(JsonObserver.Obj(AllowList).Mask(json));
Console.WriteLine(JsonObserver.Obj(NullList).Mask(json));
// Output:
// {"s":"***","n":"***","b":"***","z":null}
// {"s":null,"n":null,"b":null,"z":null}
```

### Absolute and any-depth rules

**Absolute** rules follow the path from the root: `Match(name)` for one level, `Path(a, b, …)` for several. **Any-depth** rules (inside `AnyDepth(...)`) match a property's name, or with `Path` the end of its path, wherever it is nested. Names match case-insensitively; `Names.StartsWith`, `EndsWith`, `Contains`, `OneOf` and `Regex` test a name differently and follow the same case option. The first rule that writes a value wins; read rules run on every match.

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var absolute = JsonObserver.Obj(root => root
    .Match("order").Obj(order => order
        .Match("id").Unmasked()
        .Match("status").Unmasked()));

var relative = JsonObserver.Obj(AnyDepth(rules => rules
        .Path(Names.EndsWith("card"), "saved", "id").Mask("***")
        .Match(Names.Contains("email")).Mask("***"),
    BlockList));

Console.WriteLine(absolute.Mask("""{"order":{"id":42,"status":"paid","customer":"Alice","paid":true},"note":"x"}"""));
Console.WriteLine(relative.Mask("""{"s":{"MY_card":{"saved":{"id":"c-9f2a"}},"c":{"workEmail":"a@b.c","tier":"gold"}}}"""));
// Output:
// {"order":{"id":42,"status":"paid","customer":"***","paid":"***"},"note":"***"}
// {"s":{"MY_card":{"saved":{"id":"***"}},"c":{"workEmail":"***","tier":"gold"}}}
```

### Rule kinds

Every `Mask*` rule masks the **whole value whatever its JSON type** — a sensitive name never exposes a number, a boolean or the content of an object or array. An object or array under a mask rule is skipped unread.

| Rule | What the masking function receives | `null` value |
| --- | --- | --- |
| `Mask(replacement)` | a string decoded; a number or boolean as its literal (`"12.50"`, `"true"`); `null` for an object or array | stays `null`, the function is not called; `Mask(replacement, MaskNulls.Mask)` passes it |
| `MaskInt` / `MaskLong` / `MaskDecimal` | the number when it fits the type, otherwise `null` | the function receives `null` |
| `MaskBool` | `true` / `false`, otherwise `null` | the function receives `null` |
| `Mask(MaskTag)` | — the call's `ValueMaskStrategy` writes `Full` `"***"`, `Last4` `"***1234"` (shorter than 8 characters: `"***"`), `Hash` the keyed hash (the same text as Microsoft's `HmacRedactor`) or `Null` `null` | stays `null` |
| `Mask(strategy, tag)` | — this rule's own `ValueMaskStrategy` instead of the call's | stays `null` |
| `Unmasked()` | — writes a string, number, boolean or `null` unchanged | |
| `ReadStr` / `ReadInt` / `ReadLong` / `ReadDecimal` / `ReadBool` / `ReadRaw` | hands the value to the context; the default policy writes it unless `.Unmasked()` or a mask method follows on the same match; a number that does not fit arrives as `null` | |

The table holds for absolute and any-depth rules alike. A replacement is a constant string, a `Regex` whose matches become `*`, or a function of the value (and the context); a `null` result writes `null`. The function receives the whole value, and what it returns is never cut by `MaxValueBytes`, which limits values written unmasked only. `Hash` is HMAC-SHA256 with `HashKey` (a random key per process when it is empty), written exactly as Microsoft's `HmacRedactor` writes it: 16 bytes in base64, after `"<HashKeyId>:"` when a key id is set.

### Custom mask strategies

A `MaskTag` can carry a `Key` — a data classification, a redactor name — that only your `ValueMaskStrategy` interprets; the built-in strategy falls back to the tag's kind (`MaskTag.Custom(key)` becomes `"***"`). `Mask(in MaskContext, MaskValueWriter)` sees the value, its kind, the tag, the property name and the path of the value, without allocations.

```csharp
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var masker = JsonObserver.Obj(AnyDepth(rules => rules
        .Match("email").Mask(MaskTag.Custom("pii"))
        .Match("password").Mask(MaskTag.Full),
    BlockList));
var options = new JsonObserverOptions { Strategy = new LabelStrategy() };

Console.WriteLine(masker.Mask("""{"user":{"email":"a@b.c","password":"s3cret"},"items":[{"email":"x@y.z"}]}""", options));
// Output:
// {"user":{"email":"<pii at user.email>","password":"***"},"items":[{"email":"<pii at items[0].email>"}]}

sealed class LabelStrategy : ValueMaskStrategy
{
    public override void Mask(in MaskContext context, MaskValueWriter output)
    {
        if (context.Tag.TryGetKey<string>(out var label))
        {
            output.String($"<{label} at {context.Path.ToString()}>");
            return;
        }

        Default.Mask(context, output);
    }
}
```

`MaskContext` has `Value`, `Kind`, `Tag`, `Options`, `Name` (UTF-8), `IsArrayItem`, `Path` and `ValueIndex`; `MaskValueWriter` takes `ReadOnlySpan<char>` strings too, so a char-based redactor writes its result without an intermediate string, and it validates numbers. The strategy is the same for every format.

### Explain a path

`Explain` tells which rule or policy handles a path and what it does — handy to check a configuration, to document it, or to find out why a value was masked. It walks the rules exactly as the masking pass does.

```csharp
using System.Text.Json;
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var masker = JsonObserver.Obj(
    root => root.Match("lines").Array(lines => lines.Obj(line => line.Match("sku").Unmasked())),
    AnyDepth(rules => rules.Match("password").Mask("***"), AllowList));

Console.WriteLine(masker.Explain("lines[2].sku"));
Console.WriteLine(masker.Explain("lines[2].qty", ValueKind.Number));
Console.WriteLine(masker.Explain("user.password"));
// Output:
// lines[2].sku: Unchanged by Match("lines") > object item > Match("sku") → Unmasked()
// lines[2].qty: Masked by default policy AllowList → writes "***"
// user.password: Masked by AnyDepth Match("password") → Mask("***")
```

The result also lists one step per level (`Steps`) and the `Outcome`: `Unchanged`, `Masked`, `Read`, `Custom` or `Invalid`. Observers built from a `JsonShape` explain against the shape.

### Allow-list from your types

`JsonShape.FromTypeInfo` builds the expected structure from System.Text.Json metadata (naming policy, `[JsonPropertyName]`, collections, dictionaries, recursive types). Known values are written as is, sensitive ones are masked with their tag, and anything the shape does not describe is masked whole — a field the other side adds later is never logged in clear.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DragoAnt.System.Text.Json.Observer;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
var shape = JsonShape.FromTypeInfo(options.GetTypeInfo(typeof(Customer)), property => property.Name == "card" ? MaskTag.Last4 : null);
var masker = JsonObserver.FromShape(shape);

Console.WriteLine(masker.Mask("""{"name":"Alice","card":"4111111111111111","addedLater":"secret"}"""));
// Output:
// {"name":"Alice","card":"***1111","addedLater":"***"}

sealed record Customer(string Name, string Card);
```

`JsonShapeOptions` choose what happens to unknown members (`MaskWhole`, `Descend`, `PassThrough`) and whether `null` stays; `JsonShapeOptions.FromSerializerOptions(options)` also matches names with the serializer's `PropertyNameCaseInsensitive`; `UnknownTag` masks unknown values with another tag. On .NET 8, source-generated metadata carries no attributes, so classify by name there.

Every node and member keeps its metadata for integrations: `JsonShape.Members` lists `JsonShapeProperty` items with the `JsonPropertyInfo`, the CLR member, `PropertyType`, `IsRequired`, `IsNullable` and `GetCustomAttributes<T>()`, and nodes and members carry `Annotations` that an integration fills — for example from the `annotate` callback of `FromTypeInfo`.

---

## Cut-off and invalid JSON

Both APIs never throw. `MaskResult` says what happened:

| `MaskStatus` | Meaning | Output |
| --- | --- | --- |
| `Masked` | the whole payload was masked; `Flags` is empty | the masked JSON |
| `Truncated` | the payload ended early or hit `MaxOutputBytes` (`FailedAtByte` is where reading stopped), or a string was cut to `MaxValueBytes` (`FailedAtByte` is `-1`); `Flags` says which | valid JSON: the masked part, every open object and array closed |
| `Invalid` | the payload is not valid JSON, or a rule failed | valid JSON: the masked part read before the failure |
| `Unrecognized` | empty, or the root is not an object or an array | empty |

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var masker = JsonObserver.Obj(AnyDepth(rules => rules.Match("password").Mask("***"), BlockList));

var output = masker.Mask("""{"user":{"login":"alice","password":"s3cret","roles":["admin","dev""", out var result);
Console.WriteLine(output);
Console.WriteLine(result.Status);
// Output:
// {"user":{"login":"alice","password":"***","roles":["admin"]}}
// Truncated
```

### UTF-8 API for the hot path

`Mask(ReadOnlySpan<byte>, IBufferWriter<byte>, JsonObserverOptions?)` masks bytes into a writer you reuse; the string API produces exactly the same output for the same text. A payload held in several buffers, for example from a `PipeReader`, goes to `Mask(in ReadOnlySequence<byte>, …)` as is: the output is the same however the bytes are split.

```csharp
using System.Buffers;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var masker = JsonObserver.Obj(AnyDepth(rules => rules.Match("password").Mask("***"), BlockList));
var output = new ArrayBufferWriter<byte>(1024);

MaskResult result = masker.Mask("""{"user":"alice","password":"secret"}"""u8, output);
Console.WriteLine($"{result.Status} {Encoding.UTF8.GetString(output.WrittenSpan)}");
// Output:
// Masked {"user":"alice","password":"***"}
```

### Options

`JsonObserverOptions` apply to both APIs; every option but `RelaxedEscaping` and `Indented` comes from the format-neutral `ObserverOptions`:

| Option | Default | Effect |
| --- | --- | --- |
| `MaxOutputBytes` | unlimited | output limit; when reached the output is closed and the status is `Truncated` |
| `MaxValueBytes` | unlimited | longest string written unmasked; a longer one is cut, ends with `…`, and the status is `Truncated`; mask output is never cut |
| `MaxDepth` | 64 | deeper nesting is `Invalid` |
| `RelaxedEscaping` | `true` | non-ASCII and HTML characters are written unescaped |
| `HashKey`, `HashKeyId`, `Strategy` | random per process, none, built-in | used by `MaskTag` rules; `WithBase64HashKey(key)` takes a Microsoft `HmacRedactorOptions.Key` |
| `IgnoreNulls` | `false` | drops `null` properties and items, and objects and arrays left empty by that |
| `Indented` | `false` | indented output |
| `NameCaseInsensitive` | `true` | match rule names, `Names` tests (`Regex` included) and shapes ignoring case; pass the serializer's setting to match names as deserialization does |

Input may contain comments and trailing commas; a UTF-8 byte order mark is skipped. Comments are not written.

## Performance

Masking walks the tokens once. With constant-string or tag rules, the UTF-8 API allocates nothing per call once warm — the repository's allocation test pins 0 B for 1 KB and 64 KB bodies, spans and multi-segment sequences; a masking function receives a decoded `string`, which it allocates. The [benchmark report](./docs/benchmarks/benchmarks.md) compares speed and memory with a DOM masker and [JsonMasking](https://github.com/ThiagoBarradas/jsonmasking): for an 8 KB flat body the observer took 71 µs against 902 µs, and allocated 665 B against 468 KB. See the [OSS analogs comparison](./docs/comparisons/analogs.md) for how other libraries handle cut-off JSON.

## HTTP client body logging (`DragoAnt.System.Text.Json.Observer.Http`)

```sh
dotnet add package DragoAnt.System.Text.Json.Observer.Http
```

`JsonBodyLoggingHandler` logs masked JSON request and response bodies of an `HttpClient`. Register it per named client, and register an `IJsonBodyMaskerProvider` that picks the masker by body model type:

<!-- doc-test: skip -->
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
    private static readonly JsonObserver Charge = JsonObserver.Obj(rules => rules.Match("cardNumber").Mask("****"));

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

## Upgrading

See [Migrating to 2.0](./docs/migrating-to-2.0.md) and the breaking changes in the [changelog](./CHANGELOG.md).

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](./CONTRIBUTING.md). Report security issues privately as described in [SECURITY.md](./SECURITY.md).

## License

[MIT](./LICENSE)

---

*Originally written by hand and empowered by AI.*
