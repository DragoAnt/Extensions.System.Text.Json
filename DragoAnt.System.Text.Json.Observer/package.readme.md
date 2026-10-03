# DragoAnt.System.Text.Json.Observer

Mask or extract JSON values by property-path rules in a single streaming pass from `Utf8JsonReader` to `Utf8JsonWriter` — no deserialization, no DOM.

## Getting started

```sh
dotnet add package DragoAnt.System.Text.Json.Observer
```

Targets `net8.0`, `net9.0` and `net10.0`, with no dependencies beyond the .NET base class library.

## Mask

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
// {"user":{"login":"alice","password":"*****"},"card":{"number":"411111******1111","holder":"ALICE SMITH"}}
```

`BlockList` writes unmatched values unchanged. The default, `AllowList`, masks every string and number that no rule names; `NullList` writes them as `null`.

## Performance & Analogs Comparison

Under high-throughput HTTP request and response logging (e.g. 1,000+ req/s), traditional DOM-based maskers parse and re-serialize the entire JSON tree into memory, creating massive GC churn and Large Object Heap (LOH) fragmentation.

`DragoAnt.System.Text.Json.Observer` performs a **single forward streaming pass** directly from `Utf8JsonReader` to `Utf8JsonWriter` over UTF-8 bytes:

### Head-to-Head Performance (.NET 10.0 x64 RyuJIT)

| Payload Size | DragoAnt Observer (bytes) | DOM `JsonNode` | [JsonMasking 2.0](https://github.com/ThiagoBarradas/jsonmasking) (~876k dl) | Speedup vs JsonMasking | Memory Reduction |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **1 KB Flat** | **0 B** / 8.3 µs | 11,000 B / 8.0 µs | 57,633 B / 98.0 µs | **11.8× faster** | **17× less RAM** |
| **8 KB Flat** | **0 B** / 70.8 µs | 86,865 B / 61.8 µs | 468,078 B / 902.0 µs | **12.8× faster** | **703× less RAM** |
| **64 KB Flat** | **0 B** / 534.0 µs | 709,628 B (LOH!) | 3,594,477 B (LOH!) / 8,173.0 µs | **15.3× faster** | **5,364× less RAM** |

*Detailed benchmark logs and methodology: [OSS Analogs Comparison](https://github.com/DragoAnt/Extensions.System.Text.Json/blob/main/docs/comparisons/analogs.md).*

### Truncated & Incomplete JSON: Container Synthesis

When HTTP bodies are cut short by logging limits (e.g. 32 KB cap) or network timeouts:

| Library | Behavior on Truncated JSON | Security & Stability |
| :--- | :--- | :--- |
| **[DragoAnt Observer](https://github.com/DragoAnt/Extensions.System.Text.Json)** | **Synthesizes missing closing braces (`}}`)** and emits valid JSON. | ✅ **100% safe.** 0 leaks, 0 crashes. |
| **[JsonMasking](https://github.com/ThiagoBarradas/jsonmasking)** | Throws unhandled `JsonReaderException: '}' expected`. | ❌ Crash or raw body leak on fallback. |
| **[Slin.Masking](https://github.com/sw0/Slin.Masking)** | Catches exception and **returns raw input unmasked**. | 🚨 **Severe security leak** in logs/SIEM. |

### Zero-Allocation Streaming (Hot Path)

```csharp
using System.Buffers;
using DragoAnt.System.Text.Json.Observer;

var output = new ArrayBufferWriter<byte>(1024);
ReadOnlySpan<byte> utf8Json = """{"user":"alice","password":"secret"}"""u8;

MaskResult result = masking.Mask(utf8Json, output);
// 0 B heap allocated!
```

## Extract

```csharp
var observer = JsonObserver.Obj<RequestInfo>(root => root
    .Match("routing").Obj(routing => routing
        .Match("contractId").ReadInt((value, info) => info.ContractId = value)
        .Match("method").ReadStr((value, info) => info.Method = value)));

var info = new RequestInfo();
observer.Read("""{"routing":{"contractId":2,"method":"card"}}""", info);
// info: ContractId = 2, Method = "card"

sealed class RequestInfo
{
    public int? ContractId { get; set; }
    public string? Method { get; set; }
}
```

`observer.Mask(json, info)` masks and extracts in the same pass.

## Documentation & Comparisons

- [Full Documentation & Benchmarks](https://github.com/DragoAnt/Extensions.System.Text.Json#readme)
- [OSS Analogs Comparison](https://github.com/DragoAnt/Extensions.System.Text.Json/blob/main/docs/comparisons/analogs.md)
- HTTP Client Body Logging package: `DragoAnt.System.Text.Json.Observer.Http`

## Feedback

Report issues or feature requests on [GitHub Issues](https://github.com/DragoAnt/Extensions.System.Text.Json/issues).
