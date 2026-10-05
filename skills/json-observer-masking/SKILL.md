---
name: json-observer-masking
description: Mask or extract values in JSON with DragoAnt.System.Text.Json.Observer before logging it — passwords, card numbers, tokens, emails, PII — in one streaming Utf8JsonReader to Utf8JsonWriter pass, without deserializing. Covers JsonObserver.Obj/Array/Any rules, absolute vs Relative property paths, PropMatches (EndsWith, Contains, OneOf, Regex), MaskAny/MaskStr/MaskTag (Full, Last4, Hash, Omit), default policies (AllowList, BlockList, NullList), allow-lists from DTOs with JsonShape.FromTypeInfo, extracting fields into a context with Read rules, the never-throw string and UTF-8 APIs with MaskResult/MaskStatus for cut-off or invalid bodies, JsonObserverOptions limits, and allocation-free hot paths. Use when redacting a JSON payload, writing or fixing JsonObserver rules, choosing a default policy, upgrading from 1.x, or when masked output looks wrong ("everything became ***", "the secret is still visible", "booleans are masked", "output is empty").
---

# Masking JSON with DragoAnt.System.Text.Json.Observer

`JsonObserver` rewrites a JSON payload token by token: values a rule names are masked (or handed to a context object), everything else follows a **default policy**. It never builds objects or a DOM, and it **never throws** — a cut-off or invalid payload yields its masked prefix, closed into valid JSON.

Package: `DragoAnt.System.Text.Json.Observer` (net8.0, net9.0, net10.0). Namespaces: `DragoAnt.System.Text.Json.Observer`, `.Strategies` (`MaskTag`, `PropMatches`) and `using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;` (`BlockList`, `AllowList`, `NullList`, `Relative`).

## Quick start

```csharp
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var masker = JsonObserver.Obj(Relative(rules => rules
        .Match("password").MaskAny("***")
        .Match("card", "number").MaskAny(MaskTag.Last4),
    BlockList));

Console.WriteLine(masker.Mask("""{"user":"alice","password":"s3cret","card":{"number":"4111111111111111"},"active":true}"""));
// Output:
// {"user":"alice","password":"***","card":{"number":"***1111"},"active":true}
```

## Decision path

1. **Do you have the DTO the JSON comes from?** Build an allow-list from it: `JsonShape.FromTypeInfo(typeInfo, classify)` + `JsonObserver.FromShape(shape)`. Known fields pass, the ones you classify are masked by tag, anything new is masked. Safest choice for third-party payloads → [examples.md#allow-list-from-a-type](./examples.md#allow-list-from-a-type).
2. **You only know which names are sensitive** ("mask every `password`, wherever it is") → **block-list**: `JsonObserver.Obj(Relative(rules => …, BlockList))`.
3. **You know which fields are safe to show** and want everything else hidden → **allow-list rules**: absolute rules with `.Unmasked()` under the default `AllowList`.
4. **You also need values out** (an order id for a log scope) → `JsonObserver.Obj<TContext>(…)` with `Read*` rules; `Read(json, ctx)` extracts without writing → [examples.md#extract-values-while-masking](./examples.md#extract-values-while-masking).
5. **Hot path** (every request body) → the UTF-8 API `Mask(ReadOnlySpan<byte>, IBufferWriter<byte>)` with a reused `ArrayBufferWriter<byte>` → [recipes.md#hot-path-utf-8-api](./recipes.md#hot-path-utf-8-api).
6. **Body may be cut off** (a size-capped log, a stream prefix) → use the overload with `MaskResult` and look at `Status` → [recipes.md#cut-off-or-invalid-bodies](./recipes.md#cut-off-or-invalid-bodies).

## Rules that matter

1. **Build once, share everywhere.** An observer is immutable and thread-safe; keep it in a `static readonly` field. Building one per call costs far more than masking.
2. **The default policy is `AllowList`.** A factory without a policy, and `Relative(rules)` without its second argument, write every string, number **and boolean** no rule names as `"***"` (`null` stays). Pass `BlockList` to keep unnamed values.
3. **Absolute vs relative.** Rules on a builder (`Obj(root => root.Match("order").Obj(…))`) follow the path from the root. Rules inside `Relative(…)` match the **end** of a path at any depth: `Match("card", "number")` hits every `…card.number`. An **array item is one path level**, so reach `{"lines":[{"qty":…}]}` with `Match("lines", anyItem, "qty")` where `anyItem = new PropMatchingStrategy(_ => true)`. The first rule that matches wins.
4. **Names match exactly and case-insensitively.** Use `PropMatches.EndsWith/StartsWith/Contains/OneOf/Regex` for anything else; they follow the same case option.
5. **Prefer `MaskAny` for secrets.** Every `Mask*` rule masks the whole value whatever its JSON type (a number, a boolean, an object), but `MaskAny` keeps `null` as `null` without calling your function, while `MaskStr` passes `null` to it.
6. **Tags for standard masks:** `MaskAny(MaskTag.Full)` → `"***"`, `Last4` → `"***1111"` (shorter than 8 characters → `"***"`), `Hash` → `"hash:<16 hex>"`, `Omit` → `null`. Set `JsonObserverOptions.HashKey` for hashes that correlate across processes; the default key is random per process.
7. **Never throws, always safe.** Both APIs return the masked prefix of cut-off or invalid input, closed into valid JSON, and never write a masked value in clear. `MaskStatus` is `Masked`, `Truncated`, `Invalid` or `NotJson` (empty output: empty input, or a root that is not an object or array).
8. **Measure allocations, never assume zero.** With constant or tag rules, the UTF-8 API allocates a small constant amount per call; a masking function receives a `string`, which allocates.

## Pitfalls (details in [pitfalls.md](./pitfalls.md))

- "Everything became `***`" → you used the default `AllowList`; pass `BlockList` (to the factory, or as `Relative`'s second argument).
- "The secret is still visible" → the rule is absolute but the field is nested, or the name differs (`Password` vs `passwd`); use `Relative` and a `PropMatches`.
- A rule written before an `Obj(...)` rule for the same name wins and masks the whole object.
- `JsonObserver.Obj(...)` on a root array returns `Invalid`; use `JsonObserver.Any(...)` when the root can be either.
- Comments in the input are accepted and never written; a UTF-8 BOM is skipped.

## Companions

- [examples.md](./examples.md) — runnable examples: rule kinds, policies, matchers, arrays, tags, shapes, extraction.
- [recipes.md](./recipes.md) — logging a request body, hot path, cut-off bodies, limits, hashing, custom strategies.
- [pitfalls.md](./pitfalls.md) — symptoms and their causes.
- [migrating-from-1x.md](./migrating-from-1x.md) — breaking changes from 1.x with before/after.

Every C# block in these files is a complete program (top-level statements, implicit usings) that compiles against the library and prints what its `// Output:` comment shows.
