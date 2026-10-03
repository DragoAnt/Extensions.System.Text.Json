# OSS Analogs for JSON Body Masking in .NET

## Executive Summary

When building high-throughput HTTP request and response logging for production APIs (such as payment gateways, banking integrations, and high-load microservices), masking sensitive data (passwords, credit card numbers, CVVs, tokens, PII) is mandatory.

A common question is: **"Should we switch to an existing open-source library, or use DragoAnt.System.Text.Json.Observer?"**

**Conclusion:** **Do not switch to existing analogs.** Existing libraries in the .NET ecosystem suffer from severe architectural limitations for HTTP body logging:
1. **DOM Allocations:** The most popular text maskers (`JsonMasking`, `Slin.Masking`) parse the entire JSON body into an in-memory DOM (`JsonNode` / `JsonElement`), allocating 3.5×–4.5× the payload size per call, flooding the Garbage Collector (GC) and hitting the Large Object Heap (LOH) on payloads ≥64 KB.
2. **Security Leak on Truncation:** Some libraries (e.g. `Slin.Masking`) silently return the input **unmasked** when JSON is truncated (a standard occurrence when HTTP loggers cap bodies at 32 KB).
3. **Licensing:** `Slin.Masking` is licensed under **GPL-3.0** (viral copyleft), making it legally incompatible with permissive (MIT/Apache) and proprietary commercial software.
4. **Serialization Mismatch:** Libraries like `Json.Masker` operate during object serialization. At the HTTP handler/middleware layer, bodies arrive as raw byte streams; deserializing them into C# objects just to re-serialize them with masking adds enormous CPU and memory overhead.
5. **Lack of JSON Body Support:** Microsoft's official `Microsoft.Extensions.Compliance.Redaction` redacts discrete string values by classification, but does not parse or traverse JSON bodies.

**DragoAnt.System.Text.Json.Observer 2.0** is the **only** high-performance, single forward-pass streaming engine (`Utf8JsonReader` → `Utf8JsonWriter`) in .NET. On its bytes API (`ReadOnlySpan<byte>` → `IBufferWriter<byte>`), it achieves **0 B heap allocations**, fail-closed security on truncated bodies, and throughput near the raw token-copy floor.

---

## Ecosystem Taxonomy

| Category | Typical Libraries | How it works | Suitability for HTTP Body Logging |
| :--- | :--- | :--- | :--- |
| **Streaming Reader → Writer** | **DragoAnt.System.Text.Json.Observer** | Single forward pass `Utf8JsonReader` to `Utf8JsonWriter` over UTF-8 bytes. | **Optimal.** 0 B heap allocation, linear O(N) speed, stream-friendly. |
| **DOM-based Tree Walk** | `JsonMasking`, `Slin.Masking`, `Felt.Redactor.Json` | Parses JSON string into a DOM (`JsonNode`, `JsonElement`, `JToken`), traverses nodes, replaces values, re-serializes. | **Poor.** Allocates 4× payload size, causes heavy GC Gen0/Gen1/Gen2 churn. |
| **DTO Serialization Converters** | `Json.Masker.SystemTextJson`, `Byndyusoft.MaskedSerialization` | Modifies `JsonSerializer` type info or converters when serializing C# DTOs. | **Inapplicable to raw streams.** Requires 2× roundtrips (deserialize bytes → object → serialize masked). |
| **Log Destructuring / Regex** | `Destructurama.Attributed`, `Serilog.Enrichers.Sensitive` | Reflection over `{@Object}` parameters or regex scanning over logged message text. | **Unsafe & slow.** Regex misses escaped quotes, non-string primitives, and has CPU backtracking overhead. |
| **Value / Span Redactors** | `Microsoft.Extensions.Compliance.Redaction`, `Nikouu.ZeroRedact` | Redacts a single string or span value based on taxonomy/data classification. | **Incomplete.** Cannot parse JSON structures or match property paths. |

---

## Detailed Comparison Matrix

| Feature | DragoAnt Observer 2.0 | JsonMasking 2.0.0 | Slin.Masking 0.2.11 | Json.Masker.STJ 1.1.25 | MS Compliance Redaction |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Author / Maintainer** | DragoAnt | Thiago Barradas | wizardlsw | Michael Yarichuk | Microsoft |
| **NuGet Downloads** | ~1.4k | ~876k | ~32k | ~4.8k | ~4.56M |
| **License** | **MIT** | **MIT** | ⚠️ **GPL-3.0** | **MIT** | **MIT** |
| **Input Type** | `ReadOnlySpan<byte>`, `string` | `string` | `string`, `object` | C# DTO | `ReadOnlySpan<char>` |
| **Processing Engine** | `Utf8JsonReader` → `Utf8JsonWriter` | `JsonNode.Parse` (DOM) | `JsonElement` (DOM) | STJ `JsonTypeInfoModifier` | Custom `Redactor` |
| **Allocations (8 KB payload)** | **0 B** (bytes path) | ~87 KB – 112 KB | ~45 KB – 90 KB | N/A (requires DTO) | N/A (value only) |
| **Allocations (64 KB payload)**| **0 B** (bytes path) | ~710 KB – 924 KB (LOH) | ~350 KB – 700 KB (LOH)| N/A (requires DTO) | N/A (value only) |
| **Zero-Alloc Streaming API** | **Yes** (`IBufferWriter<byte>`) | ❌ No | ❌ No | ❌ No | ✅ Yes (for scalar values) |
| **Pre-encoded UTF-8 Matching**| **Yes** (zero name allocations)| ❌ No (`Regex` per property) | ❌ No (strings) | N/A | N/A |
| **Behavior on Truncated JSON**| **Fail-closed** (safe prefix) | ❌ Throws `JsonException` | 🚨 **Leaks raw body unmasked** | N/A | N/A |
| **Type-Agnostic Masking** | **Yes** (string, num, bool, obj, arr) | ❌ String values only | Partial | Per property attribute | N/A |
| **Mask & Extract in 1 Pass** | **Yes** (0 extra allocations) | ❌ No | ❌ No | ❌ No | ❌ No |
| **HTTP DelegatingHandler** | **Yes** (`.Observer.Http`) | ❌ No | ❌ No | ❌ No | ❌ (Header/path only) |

---

## In-Depth Analysis of Main Analogs

### 1. JsonMasking (ThiagoBarradas)
* **Downloads:** ~876,000
* **Approach:** Uses `System.Text.Json.Nodes.JsonNode.Parse(json)` to build a full DOM tree. Recursively walks the tree and checks each key against a list of wildcard patterns using `Regex.IsMatch`. Re-serializes the tree with `node.ToJsonString()`.
* **Drawbacks for High-Load HTTP Logging:**
  - **Memory Churn:** Parsing an 8 KB JSON request allocates ~87 KB of objects. At 1,000 req/sec, this generates **~87 MB/s of GC pressure**.
  - **LOH Fragmentation:** Payloads of 64 KB allocate over 700 KB, crossing the 85,000-byte Large Object Heap (LOH) boundary, triggering Gen2 collections.
  - **Regex Overhead:** Compiles and evaluates regular expressions per key per call.
  - **Truncation Crash:** If a request body is capped at 32 KB, `JsonNode.Parse` throws an unhandled exception.

### 2. Slin.Masking (wizardlsw)
* **Downloads:** ~32,000
* **Approach:** Uses `JsonElement.TryParseValue` with a `StringBuilder` re-emitter based on a JSON configuration profile (`masking.json`).
* **Fatal Issues:**
  - ⚠️ **GPL-3.0 License:** Highly restrictive copyleft license. Incorporating it into proprietary services or standard MIT packages risks license contamination.
  - 🚨 **Silent Security Leak:** When JSON parsing fails (e.g. malformed or truncated body), `ObjectMasker.MaskObject` catches the exception and **returns the original string unchanged**. In production, capped HTTP logs will output unmasked passwords and credit card numbers.

### 3. Microsoft.Extensions.Compliance.Redaction
* **Downloads:** ~4,560,000
* **Approach:** Introduced in .NET 8, provides an extensible classification-driven taxonomy (`DataClassification`, `Redactor`, `ErasingRedactor`, `HmacRedactor`).
* **Scope:** Operates strictly on discrete values (e.g. logging `logger.LogInformation("Processing card {Card}", card)` with redaction attributes).
* **Gap:** Does **not** inspect or redact JSON payloads. Microsoft's `AddHttpLogging` redacts HTTP headers and query route parameters, but leaves request and response bodies unmasked (or logged verbatim up to `RequestBodyLogLimit`).

### 4. Json.Masker.SystemTextJson (myarichuk)
* **Downloads:** ~4,800
* **Approach:** Uses .NET 7+ `DefaultJsonTypeInfoResolver` modifiers to inject custom converters on properties decorated with `[Sensitive]`.
* **Scope:** Ideal when you are serializing internal domain entities to JSON.
* **Gap:** In HTTP client handlers or ASP.NET Core middleware, the payload is already serialized text/bytes. You cannot use serialize-time converters without deserializing the payload into strongly typed models first, multiplying latency and allocations.

---

## Why DragoAnt.System.Text.Json.Observer 2.0 Wins

1. **True Zero-Allocation Byte Path:**
   ```csharp
   ReadOnlySpan<byte> utf8Json = ...;
   var result = observer.Mask(utf8Json, bufferWriter);
   // 0 B heap allocated!
   ```
2. **Pre-Encoded UTF-8 Property Matching:**
   Rules are compiled once into pre-encoded UTF-8 byte sequences. During traversal, property names are compared directly on `ReadOnlySpan<byte>` via case-insensitive SIMD/ASCII routines without allocating `string` instances.
3. **Fail-Closed Security by Design:**
   If a body is truncated (e.g. HTTP logger capped at 32 KB) or corrupted, Observer never leaks sensitive values. It safely flushes already-masked tokens, appends `...[truncated]`, and returns `MaskStatus.Truncated` or `MaskStatus.Invalid`.
4. **Type-Agnostic Protection:**
   A sensitive property name (e.g. `password` or `pin`) will be masked whether the JSON value is a string, a raw number (`{"pin": 1234}`), a boolean, or an entire nested object/array (`{"credentials": {"key": "value"}}`).
5. **Integrated HTTP Client Logging:**
   The companion package `DragoAnt.System.Text.Json.Observer.Http` provides `JsonBodyLoggingHandler`, seamlessly integrating with `IHttpClientFactory` and `WithBodyLogging<TReq, TResp>()` with stream-preserving replay (`PrefixRemainderStream`).
