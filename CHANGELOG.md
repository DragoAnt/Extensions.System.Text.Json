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
- **New package `DragoAnt.System.Text.Json.Observer.Http`:** `JsonBodyLoggingHandler` logs masked `HttpClient` request and response bodies; register it with `AddJsonBodyLogging`, pick maskers per body model type with `IJsonBodyMaskerProvider`, and attach model types per request with `WithBodyLogging<TRequest, TResponse>()`.

### Changed — breaking

1. **The default policy masks everything a rule does not name.** `AllowList` (still the default) writes every string, number **and boolean** as `"***"`; in 1.x it wrote `"#str#*****"` / `"#number#*****"` and kept booleans. The 1.x output is available as the obsolete `LegacyAllowList`.
2. **`Mask(string)` never throws.** It runs the same UTF-8 path as the bytes API and produces the same output for the same text: comments are skipped (never written), trailing commas are accepted, non-ASCII and HTML characters are written unescaped (`RelaxedEscaping`), invalid or cut-off text yields its masked prefix. `Mask(string, out MaskResult, options)` reports the status. The `JsonReaderOptions`, `JsonWriterOptions`, `ignoreNulls` and `ignoreComments` parameters are gone: use `JsonObserverOptions` (`IgnoreNulls`, `Indented`, `MaxDepth`).
3. **`Read(...)` never throws and returns a `MaskResult`** instead of `void`; `Read(byte[])` became `Read(ReadOnlySpan<byte>)`.
4. **Every `Mask*` rule masks the whole value whatever its JSON type.** `MaskStr`, `MaskRawValue`, `MaskInt`, `MaskLong`, `MaskDecimal` and `MaskBool` no longer pass a value of another type to the default policy (where `BlockList` exposed it), and no longer descend into an object or array: a container is skipped unread and the function receives `null`. `MaskStr` hands a number or boolean to its function as its literal (`"12.50"`, `"true"`). As these rules now match containers too, a rule written before an `Obj(...)` or `Array(...)` rule for the same name takes precedence over it.
5. **A string cut by `MaxValueBytes` reports `Truncated`** (with `FailedAtByte` `-1`), not `Masked`. A masking function receives a value longer than `MaxValueBytes` cut to that length.
6. **Number read rules no longer fail the body:** `ReadInt`, `ReadLong` and `ReadDecimal` receive `null` for a number that does not fit the type, and the token is written unchanged.
7. **`PropertyPath` is a `ref struct` valid only during the call** it is passed to: `GetPropertyName`, `GetPropertyNameReverse`, `Length` and `ToString` remain; its constructor, `MaxLength` and `Dispose` are internal. Custom rules compiled against 1.x must be rebuilt.
8. **`JsonWriter` can no longer be derived from outside the library**, `JsonWriter.FromUtf8JsonWriter` and `JsonWriter.Empty` are removed, and `WriteCommentValue` is gone (comments are never written).
9. **Internal now:** `JsonObserverException`, `PropertyPathMatch`, `JsonPropertyMatchDelegate`, `JsonPropertyPathMatchDelegate`, and the constructors of `JsonObjBuilder`, `JsonArrayBuilder`, `JsonValuePolicyBuilder` and their rule builders (start rules with `Match`).
10. **A UTF-8 byte order mark at the start of the input is skipped.**

### Observer.Http 2.0.0 — breaking (since the preview builds)

- `JsonBodyOutcome.Timeout` is replaced by `Canceled`: every cancellation, an `HttpClient.Timeout` included, is logged as `Canceled`.
- `JsonBodyLogEntry` uses `init` properties and adds `Operation`, `RequestBodyStatus` and `ResponseBodyStatus`.
- New `JsonBodyStatus` type; a body that could not be logged as JSON is a marker such as `[body not JSON]` or `[invalid JSON]`, and a truncated body is flagged.
- `JsonBodyLoggingHandler` is sealed.
- `LoggerJsonBodyLogSink` has a single constructor taking an `ILogger?`.
- `Path` no longer includes the query string, which is never logged.
- The response entry is written when the background body read finishes: the size cap is reached, the body ends, the read fails, or the caller disposes the response.
- `ElapsedMs` is the time to the response headers.
