# DragoAnt.System.Text.Json.Observer

Mask or extract JSON values by property-path rules in a single streaming pass from `Utf8JsonReader` to `Utf8JsonWriter` — no deserialization, no DOM.

[![Build](https://img.shields.io/github/actions/workflow/status/DragoAnt/Extensions.System.Text.Json/build.yml?branch=main)](https://github.com/DragoAnt/Extensions.System.Text.Json/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/DragoAnt.System.Text.Json.Observer)](https://www.nuget.org/packages/DragoAnt.System.Text.Json.Observer)
[![Downloads](https://img.shields.io/nuget/dt/DragoAnt.System.Text.Json.Observer)](https://www.nuget.org/packages/DragoAnt.System.Text.Json.Observer)
[![License](https://img.shields.io/github/license/DragoAnt/Extensions.System.Text.Json)](https://github.com/DragoAnt/Extensions.System.Text.Json/blob/main/LICENSE)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)

## Features

- **Mask** sensitive values (cards, passwords, emails, IPs) with a regex, a constant or your own function.
- **Extract** values into a typed context object, or mask and extract in the same pass.
- **Absolute rules** (`order` → `id`) and **relative rules** that match a path suffix at any depth (`*card` → `saved` → `id`).
- **Safe by default:** with no default policy, every string and number that no rule names is masked.
- Property names match case-insensitively; matchers for `StartsWith`, `EndsWith`, `Contains`, `Regex`, `OneOf`.
- Optional indented output, comment handling and dropping of `null` properties.
- Not a serializer, a DOM (`JsonNode` / `JsonDocument`) or a JSONPath engine: it rewrites or reads the token stream it is given.

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

### Reader and writer options

`Mask` also takes `JsonReaderOptions` (for example `CommentHandling = JsonCommentHandling.Allow`), `JsonWriterOptions` (for example `Indented = true`), `ignoreNulls` and `ignoreComments`.

## Compatibility

Targets `net8.0`, `net9.0` and `net10.0`. No dependencies beyond the .NET base class library.

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](./CONTRIBUTING.md). Report security issues privately as described in [SECURITY.md](./SECURITY.md).

## License

[MIT](./LICENSE)
