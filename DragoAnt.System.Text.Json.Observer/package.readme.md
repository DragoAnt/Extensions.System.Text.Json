# DragoAnt.System.Text.Json.Observer

Mask or extract JSON values by property-path rules in a single streaming pass from `Utf8JsonReader` to `Utf8JsonWriter` — no deserialization, no DOM. Built for logging: it never throws, even on cut-off or invalid JSON, and never writes a masked value in clear.

Targets `net8.0` and `net10.0`; depends only on [DragoAnt.Observer.Core](https://www.nuget.org/packages/DragoAnt.Observer.Core), whose `DragoAnt.Observer` namespace (`MaskTag`, `MaskResult`, `ValuePolicy`, …) the package imports into every C# file (`<DragoAntObserverImplicitUsing>false</DragoAntObserverImplicitUsing>` opts out).

## Quick start

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

Build an observer once and share it: it is thread-safe. `AnyDepth` rules match the end of a property's path at any depth; rules passed straight to `JsonObserver.Obj(root => …)` follow the path from the root. Values no rule names get the default policy: `AllowList` (the default) writes every string, number and boolean as `"***"`, `BlockList` writes them unchanged, `NullList` as `null`.

Every `Mask*` rule masks the whole value whatever its JSON type; an object or array under a mask rule is skipped unread.

## Mask and extract

```csharp
using DragoAnt.System.Text.Json.Observer;

var observer = JsonObserver.Obj<Order>(
    rules => rules
        .Match("id").ReadInt((id, order) => order.Id = id)
        .Match("card").Mask("***"),
    ValuePolicy.BlockList);

var order = new Order();
Console.WriteLine(observer.Mask("""{"id":42,"card":"4111111111111111"}""", order));
Console.WriteLine(order.Id);
// Output:
// {"id":42,"card":"***"}
// 42

sealed class Order
{
    public int? Id { get; set; }
}
```

## Cut-off JSON and the UTF-8 API

`MaskResult.Status` is `Masked`, `Truncated` (the payload ended early, hit `MaxOutputBytes`, or a string was cut to `MaxValueBytes`), `Invalid` or `Unrecognized`. Whatever the status, the output is valid JSON holding only masked values. The string and the UTF-8 API produce the same output.

```csharp
using System.Buffers;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var masker = JsonObserver.Obj(AnyDepth(rules => rules.Match("password").Mask("***"), BlockList));
var output = new ArrayBufferWriter<byte>(1024);

MaskResult result = masker.Mask("""{"user":"alice","password":"secret","roles":["admin","de"""u8, output);
Console.WriteLine($"{result.Status} {Encoding.UTF8.GetString(output.WrittenSpan)}");
// Output:
// Truncated {"user":"alice","password":"***","roles":["admin"]}
```

## More

- [Documentation](https://github.com/DragoAnt/Extensions.System.Text.Json#readme): rule kinds, custom mask strategies, `Explain(path)`, allow-lists from your types (`JsonShape`), `ReadOnlySequence<byte>` input, options, performance.
- [Changelog](https://github.com/DragoAnt/Extensions.System.Text.Json/blob/main/CHANGELOG.md), including the breaking changes from 1.x.
- `DragoAnt.System.Text.Json.Observer.Http` logs masked `HttpClient` request and response bodies.
- [Issues](https://github.com/DragoAnt/Extensions.System.Text.Json/issues)
