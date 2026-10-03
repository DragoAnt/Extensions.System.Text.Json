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

## Performance: 0 B Allocations on Byte Streams

Under high-load HTTP request logging (e.g. 1,000+ req/s), traditional DOM-based maskers (`JsonNode.Parse`) allocate 3.5×–4.5× the body size into the managed heap per call, causing severe GC Gen0/Gen1/Gen2 churn.

`DragoAnt.System.Text.Json.Observer` performs a **single forward streaming pass** directly from `Utf8JsonReader` to `Utf8JsonWriter`:

| Payload Size | DragoAnt Observer (bytes) | DOM `JsonNode` / `JsonMasking` |
| :--- | :---: | :---: |
| **1 KB** | **0 B** | 11 000 B *(3.3×)* |
| **8 KB** | **0 B** | 86 865 B – 112 953 B *(3.5×–4.6×)* |
| **64 KB** | **0 B** | 709 609 B – 923 894 B *(LOH!)* |

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
