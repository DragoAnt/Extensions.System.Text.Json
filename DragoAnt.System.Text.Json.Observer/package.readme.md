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

## Documentation

Absolute and relative rules, custom strategies and options: https://github.com/DragoAnt/Extensions.System.Text.Json#readme

## Feedback

Issues: https://github.com/DragoAnt/Extensions.System.Text.Json/issues
