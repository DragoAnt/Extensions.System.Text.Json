# Examples — json-observer-masking

Each block is a complete program: create a console project, reference `DragoAnt.System.Text.Json.Observer`, paste it into `Program.cs`. The `// Output:` comment is what it prints.

## Default policies

The default policy decides what happens to a value no rule names. It is the last argument of `JsonObserver.Obj/Array/Any`, of `Relative(...)`, and of nested `Obj(...)`/`Array(...)` rules (a nested rule inherits the enclosing one when it is omitted).

| Policy | `{"s":"x","n":1,"b":true,"z":null}` becomes |
| --- | --- |
| `AllowList` (the default) | `{"s":"***","n":"***","b":"***","z":null}` |
| `BlockList` | unchanged — only values a rule names are masked |
| `NullList` | `{"s":null,"n":null,"b":null,"z":null}` |

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

const string json = """{"s":"x","n":1,"b":true,"z":null}""";

Console.WriteLine(JsonObserver.Obj(AllowList).Mask(json));
Console.WriteLine(JsonObserver.Obj(BlockList).Mask(json));
Console.WriteLine(JsonObserver.Obj(NullList).Mask(json));
Console.WriteLine(JsonObserver.Obj(root => root.Match("n").Unmasked()).Mask(json));
// Output:
// {"s":"***","n":"***","b":"***","z":null}
// {"s":"x","n":1,"b":true,"z":null}
// {"s":null,"n":null,"b":null,"z":null}
// {"s":"***","n":1,"b":"***","z":null}
```

## Absolute rules

Absolute rules follow the path from the root: one `Match` per level with a nested `Obj(...)`, or several names in one `Match`. Under the default `AllowList` this is an allow-list: name what may be shown with `Unmasked()`.

```csharp
using DragoAnt.System.Text.Json.Observer;

var masker = JsonObserver.Obj(root => root
    .Match("id").Unmasked()
    .Match("order").Obj(order => order
        .Match("status").Unmasked()
        .Match("total").Unmasked())
    .Match("customer", "country").Unmasked());

Console.WriteLine(masker.Mask("""
    {"id":7,"order":{"status":"paid","total":9.5,"note":"leave at door"},"customer":{"name":"Alice","country":"NL"}}
    """));
// Output:
// {"id":7,"order":{"status":"paid","total":9.5,"note":"***"},"customer":{"name":"***","country":"NL"}}
```

## Relative rules and name matchers

`Relative(rules, defaultPolicy)` is a policy whose rules match the **end** of a property path at any depth. A plain string is an exact, case-insensitive name; `PropMatches` tests names differently. The first matching rule wins.

```csharp
using System.Text.RegularExpressions;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var masker = JsonObserver.Obj(Relative(rules => rules
        .Match(PropMatches.EndsWith("token")).MaskAny("***")
        .Match(PropMatches.Contains("email")).MaskAny("***")
        .Match(PropMatches.OneOf("pwd", "passwd", "password")).MaskAny("***")
        .Match(PropMatches.Regex(new Regex("^x-api-", RegexOptions.IgnoreCase))).MaskAny("***")
        .Match("card", "cvv").MaskAny("***"),
    BlockList));

Console.WriteLine(masker.Mask("""
    {"auth":{"AccessToken":"a1","refresh_token":"r2"},"user":{"WorkEmail":"a@b.c","PWD":"p"},"headers":{"X-Api-Key":"k"},"payment":{"card":{"cvv":123,"brand":"visa"}}}
    """));
// Output:
// {"auth":{"AccessToken":"***","refresh_token":"***"},"user":{"WorkEmail":"***","PWD":"***"},"headers":{"X-Api-Key":"***"},"payment":{"card":{"cvv":"***","brand":"visa"}}}
```

Absolute and relative rules combine: absolute rules first, then a `Relative(...)` policy for everything they do not name.

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var masker = JsonObserver.Obj(
    root => root.Match("debug").MaskAny("[removed]"),
    Relative(rules => rules.Match("password").MaskAny("***"), BlockList));

Console.WriteLine(masker.Mask("""{"debug":{"trace":"…"},"login":{"user":"bob","password":"p"}}"""));
// Output:
// {"debug":"[removed]","login":{"user":"bob","password":"***"}}
```

## Rule kinds

Every `Mask*` rule masks the whole value, whatever its JSON type; an object or array under a mask rule is skipped unread.

| Rule | The function receives | A `null` value |
| --- | --- | --- |
| `MaskAny("***")` / `MaskAny((value, ctx) => …)` | a string decoded; a number or boolean as its literal (`"12.50"`, `"true"`); `null` for an object or array | stays `null`; the function is not called |
| `MaskStr(...)` | the same as `MaskAny` | may reach the function as `null` |
| `MaskRawValue(...)` | the same, but a string as raw JSON text, escapes kept | may reach the function as `null` |
| `MaskInt` / `MaskLong` / `MaskDecimal` | the number when it fits, otherwise `null` | may reach the function as `null` |
| `MaskBool` | `true`/`false`, otherwise `null` | may reach the function as `null` |
| `MaskAny(MaskTag)` | — written by the tag strategy | stays `null` |
| `Unmasked()` | — a string, number, boolean or `null` written unchanged | stays `null` |

A strategy is a constant string, a `Regex` whose matches become `*`, or a function; a function returning `null` writes `null`. Write functions so that a `null` input returns `null` (or a constant): whether a JSON `null` reaches a `MaskStr`-family function differs between absolute and relative rules.

```csharp
using System.Text.RegularExpressions;
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var masker = JsonObserver.Obj(Relative(rules => rules
        .Match("pin").MaskAny("***")
        .Match("phone").MaskStr(new Regex("[0-9](?=[0-9]{2})"))
        .Match("amount").MaskAny((value, _) => value is null ? null : $"<{value.Length} chars>")
        .Match("age").MaskInt((age, _) => age >= 18 ? "adult" : "minor")
        .Match("address").MaskAny("***")
        .Match("note").MaskAny("***"),
    BlockList));

Console.WriteLine(masker.Mask("""
    {"pin":1234,"phone":"+31612345678","amount":12.50,"age":41,"address":{"street":"Main 1","city":"Delft"},"note":null}
    """));
// Output:
// {"pin":"***","phone":"+*********78","amount":"<5 chars>","age":"adult","address":"***","note":null}
```

## Arrays and the root type

`JsonObserver.Obj(...)` expects a root object and `JsonObserver.Array(...)` a root array; the other root is `Invalid`. `JsonObserver.Any(obj, array, policy)` accepts both. In an array builder every rule applies to every item; `Obj(...)` handles the items that are objects.

**An array item is one level of a property path.** A multi-name `Match` crosses one level per name, so `Match("lines", "sku")` never reaches `{"lines":[{"sku":…}]}`; put a match-anything test where the item is: `Match("lines", AnyItem, "sku")` with `AnyItem = new PropMatchingStrategy(_ => true)`. Use that form for **objects inside a nested array**: in 2.0.0, rules of an `Obj(...)` placed inside a property's `Array(...)` do not match (see [pitfalls.md](./pitfalls.md#rules-inside-a-nested-array-do-not-match)).

```csharp
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var anyItem = new PropMatchingStrategy(_ => true);

var rootArray = JsonObserver.Array(items => items.Obj(item => item.Match("sku").Unmasked()));

var scalarItems = JsonObserver.Obj(root => root.Match("tags").Array(tags => tags.Unmasked()));

var objectItems = JsonObserver.Obj(root => root
    .Match("lines", anyItem, "sku").Unmasked()
    .Match("lines", anyItem, "qty").Unmasked());

var either = JsonObserver.Any(_ => { }, _ => { }, Relative(rules => rules.Match("password").MaskAny("***"), BlockList));

Console.WriteLine(rootArray.Mask("""[{"sku":"A1","price":3},{"sku":"B2","price":4}]"""));
Console.WriteLine(scalarItems.Mask("""{"tags":["vip",3],"customer":"Alice"}"""));
Console.WriteLine(objectItems.Mask("""{"lines":[{"sku":"A1","qty":2,"price":3}],"customer":"Alice"}"""));
Console.WriteLine(either.Mask("""[{"password":"p"},{"user":"u"}]"""));
Console.WriteLine(either.Mask("""{"password":"p"}"""));

JsonObserver.Obj(BlockList).Mask("[1,2]", out var result);
Console.WriteLine(result.Status);
// Output:
// [{"sku":"A1","price":"***"},{"sku":"B2","price":"***"}]
// {"tags":["vip",3],"customer":"***"}
// {"lines":[{"sku":"A1","qty":2,"price":"***"}],"customer":"***"}
// [{"password":"***"},{"user":"u"}]
// {"password":"***"}
// Invalid
```

## Tags

`MaskAny(MaskTag.X)` masks with the call's `Utf8MaskStrategy` (the built-in one unless `JsonObserverOptions.MaskStrategy` sets another). `Hash` is an HMAC-SHA256 keyed by `JsonObserverOptions.HashKey`; with no key, a random key is used for the lifetime of the process.

```csharp
using System.Text;
using System.Text.RegularExpressions;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var masker = JsonObserver.Obj(Relative(rules => rules
        .Match("full").MaskAny(MaskTag.Full)
        .Match("card").MaskAny(MaskTag.Last4)
        .Match("short").MaskAny(MaskTag.Last4)
        .Match("email").MaskAny(MaskTag.Hash)
        .Match("ssn").MaskAny(MaskTag.Omit),
    BlockList));

var options = new JsonObserverOptions(HashKey: Encoding.UTF8.GetBytes("a key shared by every instance"));
var masked = masker.Mask("""{"full":true,"card":"4111111111111111","short":"1234567","email":"a@b.c","ssn":"123-45-6789"}""", options)!;
var hash = Regex.Match(masked, "hash:[0-9a-f]{16}").Value;

Console.WriteLine(masked.Replace(hash, "hash:…"));
Console.WriteLine(masker.Mask("""{"email":"a@b.c"}""", options) == $$"""{"email":"{{hash}}"}""");
// Output:
// {"full":"***","card":"***1111","short":"***","email":"hash:…","ssn":null}
// True
```

## Allow-list from a type

`JsonShape.FromTypeInfo(typeInfo, classify)` builds the expected structure from System.Text.Json metadata: names after the naming policy and `[JsonPropertyName]`, lists, dictionaries and recursive types. `classify` returns a `MaskTag` for a sensitive property and `null` for one shown as is. `JsonObserver.FromShape(shape, options)` then writes known values as they are, masks the classified ones, and handles unknown members by `JsonShapeOptions.Unknown`:

| `UnknownMemberPolicy` | An unknown member |
| --- | --- |
| `MaskWhole` (default) | is written as `"***"`, whatever its type |
| `Descend` | objects and arrays are walked, names stay visible, every value is `"***"` |
| `PassThrough` | is written as is; only classified members are masked |

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
var shape = JsonShape.FromTypeInfo(
    jsonOptions.GetTypeInfo(typeof(Customer)),
    property => property.AttributeProvider?.IsDefined(typeof(SensitiveAttribute), inherit: true) == true ? MaskTag.Last4 : null);

var maskWhole = JsonObserver.FromShape(shape);
var descend = JsonObserver.FromShape(shape, new JsonShapeOptions(UnknownMemberPolicy.Descend));

const string json = """{"name":"Alice","card_no":"4111111111111111","tags":["vip"],"extra":{"risk":"high"}}""";
Console.WriteLine(maskWhole.Mask(json));
Console.WriteLine(descend.Mask(json));
// Output:
// {"name":"Alice","card_no":"***1111","tags":["vip"],"extra":"***"}
// {"name":"Alice","card_no":"***1111","tags":["vip"],"extra":{"risk":"***"}}

[AttributeUsage(AttributeTargets.Property)]
sealed class SensitiveAttribute : Attribute;

sealed class Customer
{
    public string? Name { get; set; }

    [Sensitive]
    [JsonPropertyName("card_no")]
    public string? CardNumber { get; set; }

    public List<string>? Tags { get; set; }
}
```

On .NET 8, metadata from a source-generated `JsonSerializerContext` carries no attributes; classify by `property.Name` there. A shape can also be written by hand, and is frozen once an observer is built from it:

```csharp
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;

var shape = JsonShape.Object(
    ("id", JsonShape.Scalar),
    ("token", JsonShape.Masked(MaskTag.Full)),
    ("items", JsonShape.Array(JsonShape.Object(("sku", JsonShape.Scalar)))),
    ("labels", JsonShape.Map(JsonShape.Scalar)));

Console.WriteLine(JsonObserver.FromShape(shape).Mask("""
    {"id":1,"token":"t","items":[{"sku":"A","price":2}],"labels":{"env":"prod"},"other":true}
    """));
// Output:
// {"id":1,"token":"***","items":[{"sku":"A","price":"***"}],"labels":{"env":"prod"},"other":"***"}
```

## Extract values while masking

`JsonObserver.Obj<TContext>(...)` adds `Read*` rules that hand a value to a context object and write it unchanged. The context-aware policies live in `JsonObserverValuePolicies<TContext>`. `Mask(json, context)` masks and extracts in one pass; `Read(json, context)` only extracts and returns a `MaskResult`.

```csharp
using DragoAnt.System.Text.Json.Observer;

var observer = JsonObserver.Obj<OrderInfo>(
    root => root
        .Match("orderId").ReadLong((id, info) => info.OrderId = id)
        .Match("total").ReadDecimal((total, info) => info.Total = total)
        .Match("customer", "email").MaskAny("***"),
    JsonObserverValuePolicies<OrderInfo>.BlockList);

const string json = """{"orderId":1001,"total":19.90,"customer":{"email":"a@b.c","tier":"gold"}}""";

var info = new OrderInfo();
Console.WriteLine(observer.Mask(json, info));
Console.WriteLine(FormattableString.Invariant($"{info.OrderId} {info.Total}"));

var readOnly = new OrderInfo();
var result = observer.Read(json, readOnly);
Console.WriteLine($"{result.Status} {readOnly.OrderId}");
// Output:
// {"orderId":1001,"total":19.90,"customer":{"email":"***","tier":"gold"}}
// 1001 19.90
// Masked 1001

sealed class OrderInfo
{
    public long? OrderId { get; set; }
    public decimal? Total { get; set; }
}
```

A number that does not fit the read type (a fraction for `ReadInt`, a 30-digit integer) reaches the callback as `null`; the token is still written unchanged.
