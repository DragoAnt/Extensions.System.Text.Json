# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-10-03

### Added
- **Zero-Allocation Byte Streaming API:** `JsonObserver.Mask(ReadOnlySpan<byte> utf8, IBufferWriter<byte> output, JsonObserverOptions? options)` executing with **0 B heap allocations**.
- **Never-Throw Fail-Closed Safety:** `MaskResult(MaskStatus Status, int BytesWritten, long FailedAtByte)` with `MaskStatus` (`Masked`, `Truncated`, `NotJson`, `Invalid`). Safely flushes already-masked tokens with `...[truncated]` marker on cut-off bodies; never throws and never leaks raw unmasked tokens.
- **Pre-encoded UTF-8 Name Matcher:** Property names match directly on raw UTF-8 bytes using pre-computed matcher byte spans without allocating intermediate `string` instances.
- **Type-Agnostic Masking (`MaskAny`):** Masking a sensitive property protects numbers, booleans, objects, and arrays (via `reader.Skip()`), emitting a single masked token instead of descending and exposing sub-elements.
- **Tagged Mask Strategies (`MaskTag` / `MaskKind`):** Built-in support for `Full`, `Last4`, `Hash` (keyed HMAC-SHA256), and `Omit` (null) without per-call delegate allocations.
- **Shape-Driven Allow-List (`JsonShape`):** Build compile-time/runtime allow-lists from `JsonTypeInfo` metadata via `JsonShape.FromTypeInfo(...)`, automatically masking unknown or undeclared properties (`UnknownMemberPolicy.MaskWhole`).
- **New Package `DragoAnt.System.Text.Json.Observer.Http`:**
  - `JsonBodyLoggingHandler`: Stream-preserving `DelegatingHandler` for `HttpClient`.
  - `PrefixRemainderStream`: Transparently replays logged response streams so callers read full payloads even when capped.
  - `WithBodyLogging<TReq, TResp>` and `AddJsonBodyLogging` extensions for `IHttpClientBuilder`.

### Changed (Breaking Changes)
1. **PropertyPath Buffer Growth & Pooling:** Path buffer now dynamically expands beyond 16 levels without throwing `IndexOutOfRangeException`. `PropertyPath` is managed as a pooled, disposable resource.
2. **Deterministic Build-Time Policy Resolution:** Default value policies are resolved and bound during observer construction, eliminating race conditions and delegate mutation (`??=`) on first use.
3. **Verbatim Pass-Through Tokens:** Pass-through strings and numbers are copied verbatim via raw byte spans instead of deserializing and re-parsing. Preserves full number fidelity (`1.50`, `1e2`, `-0`, 20-digit ints, `1e400`).
4. **UTF-8 Name Matching:** `reader.GetString()` is no longer invoked for unescaped property names; name matching evaluates UTF-8 byte spans.
5. **Universal Token Masking:** Rules targeting sensitive names no longer pass through non-string primitives (numbers, booleans) or nested containers when using `BlockList`.
6. **Preservation of JSON Token Types during Extraction:** `Read*` rules (`ReadInt`, `ReadDecimal`, `ReadBool`) write the original JSON token verbatim into the output stream instead of converting values to strings (`"42"` -> `42`).
7. **Never-Throw Output Contract:** `Mask` over byte spans returns a structured `MaskResult` rather than throwing `JsonException` or `JsonReaderException`.
8. **Tagged Strategy Dispatch:** Replaced per-call delegate allocations with lightweight `MaskTag` records.
9. **Shape Allow-List Replaces Legacy AllowList:** The legacy `AllowList` behavior (which output `#str#*****` placeholders and descended unknown subtrees) is replaced by `JsonShape.FromTypeInfo` with `UnknownMemberPolicy.MaskWhole`. Legacy allow list is deprecated as `LegacyAllowList`.
