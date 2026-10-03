# Pitfalls — json-observer-masking

Symptom first, then the cause and the fix. Each block is a complete program.

## Everything became `"***"`

**Cause:** the default policy is `AllowList`. `JsonObserver.Obj(rules)` without a second argument, and `Relative(rules)` without its second argument, mask every string, number and boolean no rule names.

**Fix:** pass `BlockList` when only the named values are sensitive.

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

const string json = """{"user":"alice","password":"p","active":true}""";

Console.WriteLine(JsonObserver.Obj(Relative(rules => rules.Match("password").MaskAny("x"))).Mask(json));
Console.WriteLine(JsonObserver.Obj(Relative(rules => rules.Match("password").MaskAny("x"), BlockList)).Mask(json));
// Output:
// {"user":"***","password":"x","active":"***"}
// {"user":"alice","password":"x","active":true}
```

## The secret is still visible

Check, in order:

1. **The rule is absolute, the field is nested.** `JsonObserver.Obj(root => root.Match("password")…)` only matches a top-level `password`. Use `Relative(...)` to match at any depth.
2. **The name differs.** Matching is exact (case-insensitive): `password` does not match `newPassword` or `passwd`. Use `PropMatches.Contains("password")` or `PropMatches.OneOf(...)`.
3. **The path crosses an array.** An array item is a path level: `Match("users", "password")` does not reach `{"users":[{"password":…}]}`. Use a single name in `Relative(...)`, or `Match("users", AnyItem, "password")` with `AnyItem = new PropMatchingStrategy(_ => true)`.
4. **The value is not a string and the rule only reads.** `Read*` rules write the value unchanged; use a `Mask*` rule.

```csharp
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

const string json = """{"login":{"newPassword":"p1"},"users":[{"password":"p2"}]}""";

var tooNarrow = JsonObserver.Obj(root => root.Match("password").MaskAny("***"), BlockList);
var fixedRules = JsonObserver.Obj(Relative(rules => rules.Match(PropMatches.Contains("password")).MaskAny("***"), BlockList));

Console.WriteLine(tooNarrow.Mask(json));
Console.WriteLine(fixedRules.Mask(json));
// Output:
// {"login":{"newPassword":"p1"},"users":[{"password":"p2"}]}
// {"login":{"newPassword":"***"},"users":[{"password":"***"}]}
```

## Rules inside a nested array do not match

**Known issue in 2.0.0:** the rules of an `Obj(...)` placed inside a property's `Array(...)` — `root.Match("lines").Array(l => l.Obj(line => line.Match("qty")…))` — never match. Under `BlockList` the value stays in clear; under `AllowList` everything in the item is masked. An `Obj(...)` directly under a root `JsonObserver.Array(...)` works.

**Fix:** address the items with a path that names the item level, or with relative rules.

```csharp
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var anyItem = new PropMatchingStrategy(_ => true);
const string json = """{"lines":[{"sku":"A1","qty":2}]}""";

var broken = JsonObserver.Obj(root => root.Match("lines").Array(lines => lines.Obj(line => line.Match("qty").MaskAny("***"))), BlockList);
var byPath = JsonObserver.Obj(root => root.Match("lines", anyItem, "qty").MaskAny("***"), BlockList);
var byRelative = JsonObserver.Obj(Relative(rules => rules.Match("lines", anyItem, "qty").MaskAny("***"), BlockList));

Console.WriteLine(broken.Mask(json));
Console.WriteLine(byPath.Mask(json));
Console.WriteLine(byRelative.Mask(json));
// Output:
// {"lines":[{"sku":"A1","qty":2}]}
// {"lines":[{"sku":"A1","qty":"***"}]}
// {"lines":[{"sku":"A1","qty":"***"}]}
```

## A whole object was replaced by `"***"`

**Cause:** every `Mask*` rule matches containers too and masks them whole. A mask rule written **before** an `Obj(...)`/`Array(...)` rule for the same name wins, because the first matching rule wins.

**Fix:** put the `Obj(...)` rule first, or use a more specific name.

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

const string json = """{"card":{"brand":"visa","number":"4111"}}""";

var maskFirst = JsonObserver.Obj(root => root
    .Match("card").MaskAny("***")
    .Match("card").Obj(card => card.Match("number").MaskAny("***")), BlockList);

var objFirst = JsonObserver.Obj(root => root
    .Match("card").Obj(card => card.Match("number").MaskAny("***"))
    .Match("card").MaskAny("***"), BlockList);

Console.WriteLine(maskFirst.Mask(json));
Console.WriteLine(objFirst.Mask(json));
// Output:
// {"card":"***"}
// {"card":{"brand":"visa","number":"***"}}
```

## The output is empty

**Cause:** `MaskStatus.NotJson` — the input is empty, or its root is a string or number. Or `Invalid` with nothing read: a root array given to `JsonObserver.Obj(...)` (or a root object to `JsonObserver.Array(...)`).

**Fix:** check `MaskResult.Status`; use `JsonObserver.Any(...)` when the root can be an object or an array.

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var objOnly = JsonObserver.Obj(BlockList);
var any = JsonObserver.Any(_ => { }, _ => { }, BlockList);

Console.WriteLine($"[{objOnly.Mask("[1]", out var r1)}] {r1.Status}");
Console.WriteLine($"[{objOnly.Mask("\"text\"", out var r2)}] {r2.Status}");
Console.WriteLine($"[{any.Mask("[1]", out var r3)}] {r3.Status}");
// Output:
// [] Invalid
// [] NotJson
// [[1]] Masked
```

## `Hash` values differ between services or restarts

**Cause:** with no `JsonObserverOptions.HashKey`, the key is random per process. **Fix:** set the same `HashKey` everywhere ([recipes.md](./recipes.md#correlate-masked-values-across-services)).

## `Last4` shows `"***"` instead of the last digits

**Cause:** values shorter than 8 characters are masked fully, so a short value is not narrowed down. Booleans, objects and arrays are always `"***"`.

## A masking function sees a shortened value

**Cause:** `MaxValueBytes` also cuts the value handed to a function. The result reports `Truncated`.

## Building an observer per call

An observer compiles its rules when built and caches path buffers across calls. Building one per message costs far more than masking; keep it in a `static readonly` field or a singleton and share it across threads.

## Expecting zero allocations

With constant or tag rules the UTF-8 API allocates a small constant amount per call (a few hundred bytes, whatever the body size). Masking functions receive a `string`, and the string API allocates the input and output strings. Set allocation budgets from measurements, not from "0 B".
