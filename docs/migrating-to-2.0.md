# Migrating to 2.0

2.0 moves the format-neutral types into [DragoAnt.Observer.Core](https://www.nuget.org/packages/DragoAnt.Observer.Core) (namespace `DragoAnt.Observer`, imported into every C# file by the package), renames the builder verbs once, and changes a few defaults. Every break is listed in the [changelog](../CHANGELOG.md); this page is the lookup table.

**Build requirement (coming in DragoAnt.Observer 1.1 as a warning, 1.2 as an error):** the .NET 10 SDK. 2.0 itself builds with any .NET 8+ SDK, and apps may keep targeting `net8.0`; `<DragoAntObserverAllowOldCompiler>true</DragoAntObserverAllowOldCompiler>` will keep the check a warning.

## Renames

| 1.0.2 | 2.0 |
| --- | --- |
| `using DragoAnt.System.Text.Json.Observer.Strategies;` | nothing — `DragoAnt.Observer` is imported by the package |
| `using static …JsonObserverValuePolicies;` | `using static DragoAnt.Observer.ValuePolicy;` and `using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;` |
| `JsonObserverValuePolicies<T>.BlockList` / `AllowList` / `NullList` | `ValuePolicy.BlockList` / `AllowList` / `NullList` (any context type) |
| `Relative(rules => …, policy)` | `AnyDepth(rules => …, policy)` |
| `Match("card", "number")` (several names) | `Path("card", "number")`; `Match(name)` stays |
| `MaskAny(x)` | `Mask(x)` |
| `MaskStr(x)` | `Mask(x, MaskNulls.Mask)` |
| `MaskRawValue(f)` | `Mask(f, MaskNulls.Mask)` — the function gets the decoded string |
| `MaskTag.Omit` / `MaskKind.Omit` | `MaskTag.Null` / `MaskKind.Null` |
| `new MaskTag(kind, key)` | `MaskTag.Create(kind, key)` |
| `PropMatches.X(…)` / `PropMatchingStrategy` | `Names.X(…)` / `NameMatch` |
| `PropertyPath` (`GetPropertyName`, `GetPropertyNameReverse`, `TryGetPropertyNameUtf8`, `TryGetArrayIndex`, `IsArrayItem`) | `DataPath` (`GetName`, `GetNameFromEnd`, `TryGetName`, `TryGetItemIndex`, `IsItem`) |
| `JsonObserveringEmptyContext` | `NoContext` |
| `JsonPathExplanation` / `JsonPathOutcome` | `PathExplanation` / `PathOutcome` |
| `MaskStatus.NotJson` | `MaskStatus.Unrecognized` |
| `Utf8MaskStrategy`, `Utf8MaskContext`, `Mask(in Utf8MaskContext, JsonWriter)` | `ValueMaskStrategy`, `MaskContext`, `Mask(in MaskContext, MaskValueWriter)` |
| `context.TokenType` / `context.PropertyName` | `context.Kind` (`ValueKind`) / `context.Name` |
| `JsonObserverOptions(MaxValueBytes: 8)` | `new JsonObserverOptions { MaxValueBytes = 8 }` |
| `MaskStrategy` / `PropertyNameCaseInsensitive` | `Strategy` / `NameCaseInsensitive` |
| `new JsonShapeOptions(UnknownMemberPolicy.Descend)` | `new JsonShapeOptions { Unknown = UnknownMemberPolicy.Descend }` |
| `Explain(path, JsonTokenType.Number)` | `Explain(path, ValueKind.Number)` |
| custom rule `(ref Utf8JsonReader r, JsonWriter w, T c, ref PropertyPath p) => …` | `(ref JsonValueContext<T> c) => …` with `c.Reader`, `c.Writer`, `c.Context`, `c.Path`, `c.WriteDefault()` |
| a delegate as the default policy | `JsonValuePolicy.Custom(rule)` |
| `LegacyAllowList` | removed: `AllowList`, or `ValuePolicy.Tagged(tag)` |

## Before and after

<!-- doc-test: skip -->
```csharp
// 1.0.2
var masker = JsonObserver.Obj(Relative(rules => rules
        .Match("password").MaskAny("***")
        .Match("card", "number").MaskAny(MaskTag.Last4),
    BlockList));
```

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var masker = JsonObserver.Obj(AnyDepth(rules => rules
        .Match("password").Mask("***")
        .Path("card", "number").Mask(MaskTag.Last4),
    BlockList));

Console.WriteLine(masker.Mask("""{"password":"s3cret","card":{"number":"4111111111111111"}}"""));
// Output:
// {"password":"***","card":{"number":"***1111"}}
```

## The hash changes

`MaskTag.Hash` now writes exactly what Microsoft's `HmacRedactor` (Microsoft.Extensions.Compliance.Redaction) writes for the same key: HMAC-SHA256 over the value's UTF-16 text, the first 16 bytes in base64 (24 characters), after `"<HashKeyId>:"` when `HashKeyId` is set; `""` hashes to `""`. Hashes stored from the previews (`hash:` and 16 hex characters) do not match any more. Pass the `HmacRedactorOptions.Key` with `WithBase64HashKey(key)` to correlate with logs Microsoft's redactor wrote:

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

var options = new JsonObserverOptions { HashKeyId = 7 }
    .WithBase64HashKey("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw==");
var masker = JsonObserver.Obj(AnyDepth(rules => rules.Match("email").Mask(MaskTag.Hash), BlockList));

var hash = masker.Mask("""{"email":"a@b.c"}""", options)!;
Console.WriteLine(hash.StartsWith("""{"email":"7:""") && hash.Length == """{"email":"7:"}""".Length + 24);
// Output:
// True
```

## Results report why

`MaskResult.Flags` tells why a result is `Truncated` or `Invalid`. Data after the root (`{"a":1}{"b":2}`) and invalid UTF-8 replaced by U+FFFD used to report `Masked`; they report `Truncated` now, with the whole document written.

## Comments

Comments in the input are still dropped by default. `new JsonObserverOptions { Comments = CommentPolicy.BlockList }` keeps them (a comment of a masked value is written masked), `.Comment(CommentKind.Inline, CommentRules.Keep)` after a rule keeps those of the values it matches, and `CommentPolicy.DropAll` drops them whatever a rule says. Details: [comments](https://github.com/DragoAnt/Observer/blob/main/docs/comments.md).

## Target frameworks

`net9.0` is dropped: the package targets `net8.0` and `net10.0`, and a `net9.0` app uses the `net8.0` assets.
