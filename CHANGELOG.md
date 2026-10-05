# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-10-03

### Added

- **UTF-8 API:** `JsonObserver.Mask(ReadOnlySpan<byte>, IBufferWriter<byte>, JsonObserverOptions?)` masks bytes into a reusable writer. With constant or tag rules it allocates a small constant amount per call, whatever the body size.
- **Never-throw contract with `MaskResult`:** every `Mask` and `Read` overload reports `MaskStatus` (`Masked`, `Truncated`, `Invalid`, `NotJson`), the bytes written and the input offset where reading stopped. On cut-off or invalid input the output is the masked part read so far, with every open object and array closed, so it is always valid JSON and never holds a masked value in clear.
- **`JsonObserverOptions`** for both APIs: `MaxOutputBytes`, `MaxValueBytes`, `MaxDepth`, `RelaxedEscaping`, `HashKey`, `MaskStrategy`, `IgnoreNulls`, `Indented`.
- **Tag rules:** `MaskAny(MaskTag)` with `Full`, `Last4`, `Hash` (keyed HMAC-SHA256) and `Omit`, served by a replaceable `Utf8MaskStrategy`.
- **Allow-lists from your types:** `JsonShape.FromTypeInfo(...)` and `JsonObserver.FromShape(...)`; anything the shape does not describe is masked (`UnknownMemberPolicy`).
- **UTF-8 name matching:** property names are compared on their UTF-8 bytes, without creating strings.
- **Verbatim pass-through:** unmasked numbers and strings are copied as written (`1.50`, `1e400`, `-0`, 20-digit integers).
- `PropertyPath.Length`.
- **`ReadOnlySequence<byte>` input:** `Mask(in ReadOnlySequence<byte>, …)` and `Read(in ReadOnlySequence<byte>, …)` mask a payload held in several buffers, for example from a `PipeReader`, without copying it into one; the output is byte for byte what the span overload writes, however the bytes are split.
- **No per-call allocation on the bytes API:** the writers are reused per thread, so a warm `Mask`/`Read` of bytes with constant or tag rules allocates nothing (pinned by a test for the span, sequence, ignore-nulls and read paths).
- **`Explain(path)`:** `JsonObserver.Explain("lines[0].qty", JsonTokenType.Number)` returns a `JsonPathExplanation` naming the rule or policy that handles the value, its action, the outcome (`Unchanged`, `Masked`, `Read`, `Custom`, `Invalid`) and one step per level, for rule-based and shape observers.
- **Classified tags:** `MaskTag` carries an optional `Key` (a data classification, a redactor name) and `MaskKind.Custom`, so a strategy maps its own taxonomy without casting enum values; `MaskTag.Custom(key)`, `TryGetKey<T>`.
- **Strategies see where a value is:** `Utf8MaskStrategy.Mask(in Utf8MaskContext, JsonWriter)` receives the value, its JSON type, the tag, the options, the property name and the whole path without allocating. Both `Mask` overloads are virtual; a strategy overrides the one it needs.
- **`JsonWriter` span overloads:** `WriteStringValue(ReadOnlySpan<char>)`, `WritePropertyName(ReadOnlySpan<char>)`, `WriteBase64StringValue(ReadOnlySpan<byte>)` and `WriteNumberValue(double)`.
- **Array indices in paths:** `PropertyPath.ToString()` renders `items[2].sku` (names that need it as `['a.b']`); `TryGetArrayIndex`, `IsArrayItem` and `TryGetPropertyNameUtf8` give zero-allocation access.
- **Case sensitivity:** `JsonObserverOptions.PropertyNameCaseInsensitive` (default `true`) makes rules, `PropMatches` tests and shapes match names exactly when set to `false`, the way the serializer does; `JsonShapeOptions.PropertyNameCaseInsensitive` and `JsonShapeOptions.FromSerializerOptions(...)` set it for one shape observer; `PropertyPath.PropertyNameCaseInsensitive` tells a custom rule.
- **Metadata on shapes:** `JsonShape.Members` lists `JsonShapeProperty` items with the `JsonPropertyInfo`, CLR member, property and declaring type, `IsRequired`, `IsNullable` and custom attributes; nodes carry their `JsonTypeInfo`/`ClrType`; nodes and properties have `Annotations` for integrations; `FromTypeInfo` takes an `annotate` callback, and `FindMember` looks a property up by its UTF-8 name. On .NET 8, source-generated metadata has no attributes or reference-type nullability.
- **New package `DragoAnt.System.Text.Json.Observer.Http`:** `JsonBodyLoggingHandler` logs masked `HttpClient` request and response bodies; register it with `AddJsonBodyLogging`, pick maskers per body model type with `IJsonBodyMaskerProvider`, and attach model types per request with `WithBodyLogging<TRequest, TResponse>()`.

### Changed — breaking

1. **The default policy masks everything a rule does not name.** `AllowList` (still the default) writes every string, number **and boolean** as `"***"`; in 1.x it wrote `"#str#*****"` / `"#number#*****"` and kept booleans. The 1.x output is available as the obsolete `LegacyAllowList`.
2. **`Mask(string)` never throws.** It runs the same UTF-8 path as the bytes API and produces the same output for the same text: comments are skipped (never written), trailing commas are accepted, non-ASCII and HTML characters are written unescaped (`RelaxedEscaping`), invalid or cut-off text yields its masked prefix. `Mask(string, out MaskResult, options)` reports the status. The `JsonReaderOptions`, `JsonWriterOptions`, `ignoreNulls` and `ignoreComments` parameters are gone: use `JsonObserverOptions` (`IgnoreNulls`, `Indented`, `MaxDepth`).
3. **`Read(...)` never throws and returns a `MaskResult`** instead of `void`; `Read(byte[])` became `Read(ReadOnlySpan<byte>)`.
4. **Every `Mask*` rule masks the whole value whatever its JSON type.** `MaskStr`, `MaskRawValue`, `MaskInt`, `MaskLong`, `MaskDecimal` and `MaskBool` no longer pass a value of another type to the default policy (where `BlockList` exposed it), and no longer descend into an object or array: a container is skipped unread and the function receives `null`. `MaskStr` hands a number or boolean to its function as its literal (`"12.50"`, `"true"`). As these rules now match containers too, a rule written before an `Obj(...)` or `Array(...)` rule for the same name takes precedence over it.
5. **A string cut by `MaxValueBytes` reports `Truncated`** (with `FailedAtByte` `-1`), not `Masked`. The cap applies to values written unmasked only (see 12).
6. **Number read rules no longer fail the body:** `ReadInt`, `ReadLong` and `ReadDecimal` receive `null` for a number that does not fit the type; the default policy writes the token (see 11).
7. **`PropertyPath` is a `ref struct` valid only during the call** it is passed to: `GetPropertyName`, `GetPropertyNameReverse`, `Length` and `ToString` remain; its constructor, `MaxLength` and `Dispose` are internal. Custom rules compiled against 1.x must be rebuilt. `ToString` writes array items as `[index]` (`a.b[0].c`, formerly `a.b..c`).
8. **`JsonWriter` can no longer be derived from outside the library**, `JsonWriter.FromUtf8JsonWriter` and `JsonWriter.Empty` are removed, and `WriteCommentValue` is gone (comments are never written).
9. **Internal now:** `JsonObserverException`, `PropertyPathMatch`, `JsonPropertyMatchDelegate`, `JsonPropertyPathMatchDelegate`, and the constructors of `JsonObjBuilder`, `JsonArrayBuilder`, `JsonValuePolicyBuilder` and their rule builders (start rules with `Match`).
10. **A UTF-8 byte order mark at the start of the input is skipped.**
11. **Read rules no longer decide what is written.** `ReadStr`, `ReadInt`, `ReadLong`, `ReadDecimal`, `ReadBool` and `ReadRaw` hand the value to the context; the next rule on the same match or the default policy writes it, so under `AllowList` a read value is `"***"`, not clear text. Chain `.Unmasked()` to keep it clear, or a mask method to read and mask one match: `Match("ssn").ReadStr(f).MaskAny(MaskTag.Last4)`. A read rule runs wherever it stands among the rules, so `Match("ssn").MaskAny(…).Match("ssn").ReadStr(f)` reads too. `Explain` reports `Read` only for a value read and written unchanged.
12. **A masking function receives the whole value, and mask output is never cut by `MaxValueBytes`.** A `Last4`-style function sees the real last characters (`***4444`, not the ones at the cap), and `MaskTag.Hash`, a function's result and the `AllowList` stars are written whole, with status `Masked`. A function therefore decodes a long value in full.

### Fixed

- **`PropMatches.Regex` follows `PropertyNameCaseInsensitive`:** under the default case-insensitive option it now also matches names that differ in case only (`DRiverLicensE` for `^driverLicense$`), as every other matcher does; a `Regex` built with `RegexOptions.IgnoreCase` ignores case under either option. A custom name test can follow the option through the new `PropMatchingStrategy(Func<string?, StringComparison, bool>)` constructor.

- **Rules of an `Obj(...)` inside a property's `Array(...)` now apply** at any depth (`root.Match("lines").Array(l => l.Obj(…))`, and `Array(a => a.Array(b => b.Obj(…)))`). They looked for their names one or more levels too deep, so under `BlockList` those values were written in clear and under `AllowList` the whole item was masked. Only an `Obj(...)` directly under a root `Array(...)` worked.
- **Relative rules receive `null` like absolute ones:** `MaskStr`, `MaskRawValue`, `MaskInt`, `MaskLong`, `MaskDecimal`, `MaskBool` and the read rules inside `Relative(...)` are called for a JSON `null`, as the rule-kinds table documents; `MaskAny` and `MaskAny(MaskTag)` keep `null` without calling the function.

### Observer.Http 2.0.0 — breaking (since the preview builds)

- `JsonBodyOutcome.Timeout` is replaced by `Canceled`: every cancellation, an `HttpClient.Timeout` included, is logged as `Canceled`.
- `JsonBodyLogEntry` uses `init` properties and adds `Operation`, `RequestBodyStatus` and `ResponseBodyStatus`.
- New `JsonBodyStatus` type; a body that could not be logged as JSON is a marker such as `[body not JSON]` or `[invalid JSON]`, and a truncated body is flagged.
- `JsonBodyLoggingHandler` is sealed.
- `LoggerJsonBodyLogSink` has a single constructor taking an `ILogger?`.
- `Path` no longer includes the query string, which is never logged.
- The response entry is written when the background body read finishes: the size cap is reached, the body ends, the read fails, or the caller disposes the response.
- `ElapsedMs` is the time to the response headers.
