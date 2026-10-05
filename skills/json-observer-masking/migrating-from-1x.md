# Migrating from 1.x to 2.0 — json-observer-masking

2.0 keeps the shape of the builder API (`JsonObserver.Obj/Array/Any`, `Match`, `Mask*`, `Read*`), renames parts of it (`Relative` → `AnyDepth`, multi-name `Match` → `Path`, `MaskAny`/`MaskStr`/`MaskRawValue` → `Mask`), moves the format-neutral types to the `DragoAnt.Observer` namespace of [DragoAnt.Observer.Core](https://www.nuget.org/packages/DragoAnt.Observer.Core), and changes what the defaults produce. The full rename table is in the repository's `docs/migrating-to-2.0.md`. Work through this list; the "before" blocks are 1.x code and do not compile against 2.0.

## 1. The default policy masks booleans and uses one token

`AllowList` (still the default) now writes every string, number **and boolean** as `"***"`. 1.x wrote `"#str#*****"` / `"#number#*****"` and kept booleans; that output is gone (`LegacyAllowList` was removed). Update golden strings in tests, and log parsers that looked for `#str#`.

```csharp
using DragoAnt.System.Text.Json.Observer;

Console.WriteLine(JsonObserver.Obj(root => root.Match("id").Unmasked()).Mask("""{"id":1,"name":"x","vip":true}"""));
// Output:
// {"id":1,"name":"***","vip":"***"}
```

## 2. `Mask(string)` never throws, and its options moved

1.x threw on invalid JSON and took `JsonReaderOptions`, `JsonWriterOptions`, `ignoreNulls` and `ignoreComments`. 2.0 never throws, drops comments unless `JsonObserverOptions.Comments` or a comment rule keeps them, accepts trailing commas, writes non-ASCII unescaped, and takes one `JsonObserverOptions`. Drop the `try/catch` around `Mask`, and read `MaskResult` when you need to know what happened.

<!-- doc-test: skip -->
```csharp
// 1.x
try
{
    var masked = observer.Mask(json, ignoreNulls: true, writerOptions: new JsonWriterOptions { Indented = true });
}
catch (JsonException)
{
    // invalid body
}
```

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var observer = JsonObserver.Obj(BlockList);
var masked = observer.Mask("""{"a":1,"b":null,}""", out var result, new JsonObserverOptions { IgnoreNulls = true });
Console.WriteLine($"{result.Status} {masked}");
// Output:
// Masked {"a":1}
```

## 3. `Read(...)` returns a `MaskResult`

`Read` used to return `void` and throw; it now returns `MaskResult` and never throws. `Read(byte[])` became `Read(ReadOnlySpan<byte>)` (a `byte[]` converts implicitly).

```csharp
using System.Text;
using DragoAnt.System.Text.Json.Observer;

var observer = JsonObserver.Obj<Holder>(root => root.Match("id").ReadInt((id, h) => h.Id = id));
var holder = new Holder();
MaskResult result = observer.Read(Encoding.UTF8.GetBytes("""{"id":5}"""), holder);
Console.WriteLine($"{result.Status} {holder.Id}");
// Output:
// Masked 5

sealed class Holder
{
    public int? Id { get; set; }
}
```

## 4. Every `Mask*` rule masks the whole value, whatever its type

1.x `MaskStr`, `MaskRawValue`, `MaskInt`, `MaskLong`, `MaskDecimal` and `MaskBool` used to hand a value of another type to the default policy (so under `BlockList` a numeric `cvv` under `MaskStr` stayed visible) and descended into objects. Now every mask rule masks any value and skips containers unread; `Mask` receives a number or boolean as its literal. Because they also match containers, a mask rule placed before an `Obj(...)`/`Array(...)` rule for the same name now wins over it — reorder such rules.

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var observer = JsonObserver.Obj(AnyDepth(rules => rules.Match("cvv").Mask("***", MaskNulls.Mask).Match("address").Mask("***", MaskNulls.Mask), BlockList));
Console.WriteLine(observer.Mask("""{"cvv":123,"address":{"street":"Main 1"}}"""));
// Output:
// {"cvv":"***","address":"***"}
```

## 5. A string cut by `MaxValueBytes` reports `Truncated`

It used to report success. `FailedAtByte` is -1 in that case (the whole document was read). The cap applies to values written unmasked only: see 12.

## 6. Number read rules no longer fail the body

`ReadInt`, `ReadLong` and `ReadDecimal` receive `null` for a number that does not fit; the default policy writes the token.

## 7. Custom rules take one context: `JsonValueRule<TContext>`

`MaskValue`, `Obj(rule)` and `Array(rule)` take `(ref JsonValueContext<TContext> c) => …`: `c.Reader`, `c.Writer`, `c.Context`, `c.Path` (a `DataPath`, valid during the call only: `GetName`, `GetNameFromEnd`, `TryGetItemIndex`, `Length`, `ToString`) and `c.WriteDefault()`. A custom default policy is `JsonValuePolicy.Custom(rule)`. Rebuild custom rules against 2.0.

## 8. `JsonWriter` is sealed to the library

It cannot be derived from outside; `JsonWriter.FromUtf8JsonWriter`, `JsonWriter.Empty` and `WriteCommentValue` are gone.

## 9. Internal types

`JsonObserverException`, the engine's delegates, `JsonShape.FindMember` and the builder constructors are internal. Start rules with `Match(...)` or `Path(...)` on the builder you are given.

## 10. A UTF-8 byte order mark is skipped

## 11. Read rules no longer decide what is written

A `Read*` rule used to write its value unchanged, even under `AllowList`. It now only hands the value to the context; the next rule on the same match or the default policy writes it. Where you relied on clear text, chain `.Unmasked()`; to read and mask one value, chain a mask method on the read:

```csharp
using DragoAnt.System.Text.Json.Observer;

var observer = JsonObserver.Obj<Person>(root => root
    .Match("id").ReadInt((id, p) => p.Id = id).Unmasked()
    .Match("ssn").ReadStr((ssn, p) => p.Ssn = ssn).Mask(MaskTag.Last4));
var person = new Person();
Console.WriteLine(observer.Mask("""{"id":7,"ssn":"123-45-6789","name":"Kim"}""", person));
Console.WriteLine($"{person.Id} {person.Ssn}");
// Output:
// {"id":7,"ssn":"***6789","name":"***"}
// 7 123-45-6789

sealed class Person
{
    public int? Id { get; set; }
    public string? Ssn { get; set; }
}
```

## 12. Masking functions receive the whole value

A masking function used to receive a value longer than `MaxValueBytes` cut to that length, so a `Last4`-style function printed digits from the middle; a hash was cut too. The function now receives the whole value, and mask output is never cut: `MaxValueBytes` limits values written unmasked only.

## New in 2.0, worth adopting while you migrate

- The UTF-8 API with a reused `IBufferWriter<byte>` ([recipes.md](./recipes.md#hot-path-utf-8-api)).
- `Mask(MaskTag.Last4/Hash/Null)` instead of hand-written masking functions ([examples.md](./examples.md#tags)).
- `JsonShape.FromTypeInfo` + `JsonObserver.FromShape` for structure-aware allow-lists ([examples.md](./examples.md#allow-list-from-a-type)).
