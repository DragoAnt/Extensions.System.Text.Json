# OSS Analogs for JSON Body Masking in .NET

## Executive Summary

When building high-throughput HTTP request and response logging for production APIs (such as payment gateways, banking integrations, and high-load microservices), masking sensitive data (passwords, credit card numbers, CVVs, tokens, PII) is mandatory.

A common question is: **"Should we switch to an existing open-source library, or use [DragoAnt.System.Text.Json.Observer](https://github.com/DragoAnt/Extensions.System.Text.Json)?"**

**Conclusion:** **Do not switch to existing analogs.** Existing libraries in the .NET ecosystem suffer from severe architectural limitations for HTTP body logging:
1. **DOM Allocations:** The most popular text maskers ([JsonMasking](https://github.com/ThiagoBarradas/jsonmasking), [Slin.Masking](https://github.com/sw0/Slin.Masking)) parse the entire JSON body into an in-memory DOM (`JsonNode` / `JsonElement`), allocating 3.5×–4.5× the payload size per call, flooding the Garbage Collector (GC) and hitting the Large Object Heap (LOH) on payloads $\ge 64\text{ KB}$.
2. **Security Leak on Truncation:** Some libraries (e.g. [Slin.Masking](https://github.com/sw0/Slin.Masking)) silently return the input **unmasked** when JSON is truncated (a standard occurrence when HTTP loggers cap bodies at 32 KB).
3. **Licensing:** [Slin.Masking](https://github.com/sw0/Slin.Masking) is licensed under **GPL-3.0** (viral copyleft), making it legally incompatible with permissive (MIT/Apache) and proprietary commercial software.
4. **Serialization Mismatch:** Libraries like [Json.Masker](https://github.com/myarichuk/Json.Masker) operate during object serialization. At the HTTP handler/middleware layer, bodies arrive as raw byte streams; deserializing them into C# objects just to re-serialize them with masking adds enormous CPU and memory overhead.
5. **Lack of JSON Body Support:** Microsoft's official [Microsoft.Extensions.Compliance.Redaction](https://github.com/dotnet/extensions) redacts discrete string values by classification, but does not parse or traverse JSON bodies.

**[DragoAnt.System.Text.Json.Observer 2.0](https://github.com/DragoAnt/Extensions.System.Text.Json)** is the **only** high-performance, single forward-pass streaming engine (`Utf8JsonReader` → `Utf8JsonWriter`) in .NET. On its bytes API (`ReadOnlySpan<byte>` → `IBufferWriter<byte>`), it achieves **0 B heap allocations**, fail-closed security on truncated bodies, and throughput near the raw token-copy floor.

---

## Ecosystem Taxonomy

| Category | Typical Libraries | How it works | Suitability for HTTP Body Logging |
| :--- | :--- | :--- | :--- |
| **Streaming Reader → Writer** | **[DragoAnt.System.Text.Json.Observer](https://github.com/DragoAnt/Extensions.System.Text.Json)** | Single forward pass `Utf8JsonReader` to `Utf8JsonWriter` over UTF-8 bytes. | **Optimal.** 0 B heap allocation, linear $O(N)$ speed, stream-friendly. |
| **DOM-based Tree Walk** | [ThiagoBarradas/jsonmasking](https://github.com/ThiagoBarradas/jsonmasking), [sw0/Slin.Masking](https://github.com/sw0/Slin.Masking), [raramer/Felt.Redactor.Json](https://github.com/raramer/Felt.Redactor.Json) | Parses JSON string into a DOM (`JsonNode`, `JsonElement`, `JToken`), traverses nodes, replaces values, re-serializes. | **Poor.** Allocates 4× payload size, causes heavy GC Gen0/Gen1/Gen2 churn. |
| **DTO Serialization Converters** | [myarichuk/Json.Masker](https://github.com/myarichuk/Json.Masker), [Byndyusoft/Byndyusoft.MaskedSerialization](https://github.com/Byndyusoft/Byndyusoft.MaskedSerialization), [luizaes/json-data-masking](https://github.com/luizaes/json-data-masking) | Modifies `JsonSerializer` type info or converters when serializing C# DTOs. | **Inapplicable to raw streams.** Requires 2× roundtrips (deserialize bytes → object → serialize masked). |
| **Log Destructuring / Regex** | [destructurama/attributed](https://github.com/destructurama/attributed), [serilog-contrib/Serilog.Enrichers.Sensitive](https://github.com/serilog-contrib/Serilog.Enrichers.Sensitive) | Reflection over `{@Object}` parameters or regex scanning over logged message text. | **Unsafe & slow.** Regex misses escaped quotes, non-string primitives, and has CPU backtracking overhead. |
| **Value / Span Redactors** | [dotnet/extensions (Compliance.Redaction)](https://github.com/dotnet/extensions), [nikouu/ZeroRedact](https://github.com/nikouu/ZeroRedact) | Redacts a single string or span value based on taxonomy/data classification. | **Incomplete.** Cannot parse JSON structures or match property paths. |

---

## Detailed Comparison Matrix

| Feature | **DragoAnt Observer 2.0** | **MS Compliance Redaction** | **JsonMasking 2.0.0** | **Slin.Masking 0.2.11** | **Json.Masker.STJ 1.1.25** |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Links** | [📦 nuget](https://www.nuget.org/packages/DragoAnt.System.Text.Json.Observer) | [📦 nuget](https://www.nuget.org/packages/Microsoft.Extensions.Compliance.Redaction) · [🔗 source](https://github.com/dotnet/extensions) | [📦 nuget](https://www.nuget.org/packages/JsonMasking) · [🔗 source](https://github.com/ThiagoBarradas/jsonmasking) | [📦 nuget](https://www.nuget.org/packages/Slin.Masking) · [🔗 source](https://github.com/sw0/Slin.Masking) | [📦 nuget](https://www.nuget.org/packages/Json.Masker.SystemTextJson) · [🔗 source](https://github.com/myarichuk/Json.Masker) |
| **NuGet Downloads** | ~1.4k | **~4.56M** | **~876k** | ~32k | ~4.8k |
| **Author / Vendor** | DragoAnt | Microsoft | Thiago Barradas | wizardlsw | Michael Yarichuk |
| **License** | **MIT** | **MIT** | **MIT** | ⚠️ **GPL-3.0** | **MIT** |
| **Input Type** | `ReadOnlySpan<byte>`, `string` | `ReadOnlySpan<char>` | `string` | `string`, `object` | C# DTO |
| **Processing Engine** | `Utf8JsonReader` → `Utf8JsonWriter` | Custom `Redactor` | `JsonNode.Parse` (DOM) | `JsonElement` (DOM) | STJ `JsonTypeInfoModifier` |
| **[Zero-Alloc Streaming API](../benchmarks/benchmarks.md)**<br>*(example: [`BytesApiTests.cs`](../../DragoAnt.System.Text.Json.Observer.Tests.Shared/BytesApiTests.cs))* | **Yes** (`IBufferWriter<byte>`) | ✅ Yes (scalar values) | ❌ No | ❌ No | ❌ No |
| **[Allocations (8 KB payload)](../benchmarks/benchmarks.md)** | **0 B** (bytes path) | N/A (value only) | ~87 KB – 112 KB | ~45 KB – 90 KB | N/A (requires DTO) |
| **[Allocations (64 KB payload)](../benchmarks/benchmarks.md)** | **0 B** (bytes path) | N/A (value only) | ~710 KB – 924 KB (LOH) | ~350 KB – 700 KB (LOH) | N/A (requires DTO) |
| **[Pre-encoded UTF-8 Matching](../../DragoAnt.System.Text.Json.Observer.Tests.Shared/NameMatchingTests.cs)** | **Yes** (zero name allocations) | N/A | ❌ No (`Regex` per property) | ❌ No (strings) | N/A |
| **[Truncated JSON & Container Synthesis](../../DragoAnt.System.Text.Json.Observer.Tests.Shared/BytesApiTests.cs#L63-L135)** | **Fail-closed** (synthesizes closing brackets) | N/A | ❌ Throws `JsonException` | 🚨 **Leaks raw body unmasked** | N/A |
| **[Type-Agnostic Masking (`MaskAny`)](../../DragoAnt.System.Text.Json.Observer.Tests.Shared/WholeValueMaskingTests.cs)** | **Yes** (string, num, bool, obj, arr) | N/A | ❌ String values only | Partial | Per property attribute |
| **[Mask & Extract in 1 Pass](../../DragoAnt.System.Text.Json.Observer.Tests.Shared/MaskAndExtractTests.cs)** | **Yes** (0 extra allocations) | ❌ No | ❌ No | ❌ No | ❌ No |
| **[HTTP Body DelegatingHandler](../../DragoAnt.System.Text.Json.Observer.Http.Tests/JsonBodyLoggingHandlerTests.cs)** | **Yes** (`.Observer.Http`) | ❌ (Header/path only) | ❌ No | ❌ No | ❌ No |

---

## In-Depth Analysis of Main Analogs

### 1. Microsoft.Extensions.Compliance.Redaction — [dotnet/extensions](https://github.com/dotnet/extensions)
* **Package:** [Microsoft.Extensions.Compliance.Redaction on NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Compliance.Redaction)
* **Downloads:** ~4,560,000
* **Approach:** Introduced in .NET 8, provides an extensible classification-driven taxonomy (`DataClassification`, `Redactor`, `ErasingRedactor`, `HmacRedactor`).
* **Scope:** Operates strictly on discrete values (e.g. logging `logger.LogInformation("Processing card {Card}", card)` with redaction attributes).
* **Gap:** Does **not** inspect or redact JSON payloads. Microsoft's `AddHttpLogging` redacts HTTP headers and query route parameters, but leaves request and response bodies unmasked (or logged verbatim up to `RequestBodyLogLimit`).

### 2. JsonMasking — [ThiagoBarradas/jsonmasking](https://github.com/ThiagoBarradas/jsonmasking)
* **Package:** [JsonMasking on NuGet](https://www.nuget.org/packages/JsonMasking)
* **Downloads:** ~876,000
* **Approach:** Uses `System.Text.Json.Nodes.JsonNode.Parse(json)` to build a full DOM tree. Recursively walks the tree and checks each key against a list of wildcard patterns using `Regex.IsMatch`. Re-serializes the tree with `node.ToJsonString()`.
* **Drawbacks for High-Load HTTP Logging:**
  - **Memory Churn:** Parsing an 8 KB JSON request allocates ~87 KB of objects. At 1,000 req/sec, this generates **~87 MB/s of GC pressure**.
  - **LOH Fragmentation:** Payloads of 64 KB allocate over 700 KB, crossing the 85,000-byte Large Object Heap (LOH) boundary, triggering Gen2 collections.
  - **Regex Overhead:** Compiles and evaluates regular expressions per key per call.
  - **Truncation Crash:** If a request body is capped at 32 KB, `JsonNode.Parse` throws an unhandled exception.

### 3. Slin.Masking — [sw0/Slin.Masking](https://github.com/sw0/Slin.Masking)
* **Package:** [Slin.Masking on NuGet](https://www.nuget.org/packages/Slin.Masking)
* **Downloads:** ~32,000
* **Approach:** Uses `JsonElement.TryParseValue` with a `StringBuilder` re-emitter based on a JSON configuration profile (`masking.json`).
* **Fatal Issues:**
  - ⚠️ **GPL-3.0 License:** Highly restrictive copyleft license. Incorporating it into proprietary services or standard MIT packages risks license contamination.
  - 🚨 **Silent Security Leak:** When JSON parsing fails (e.g. malformed or truncated body), `ObjectMasker.MaskObject` catches the exception and **returns the original string unchanged**. In production, capped HTTP logs will output unmasked passwords and credit card numbers.

### 4. Json.Masker.SystemTextJson — [myarichuk/Json.Masker](https://github.com/myarichuk/Json.Masker)
* **Package:** [Json.Masker.SystemTextJson on NuGet](https://www.nuget.org/packages/Json.Masker.SystemTextJson)
* **Downloads:** ~4,800
* **Approach:** Uses .NET 7+ `DefaultJsonTypeInfoResolver` modifiers to inject custom converters on properties decorated with `[Sensitive]`.
* **Scope:** Ideal when you are serializing internal domain entities to JSON.
* **Gap:** In HTTP client handlers or ASP.NET Core middleware, the payload is already serialized text/bytes. You cannot use serialize-time converters without deserializing the payload into strongly typed models first, multiplying latency and allocations.

### 5. Nikouu.ZeroRedact — [nikouu/ZeroRedact](https://github.com/nikouu/ZeroRedact)
* **Package:** [Nikouu.ZeroRedact on NuGet](https://www.nuget.org/packages/Nikouu.ZeroRedact)
* **Downloads:** ~20,500
* **Approach:** Zero-allocation string and `Span<char>` redactor for discrete data types (SSNs, emails, phone numbers, credit card numbers). Excellent low-level value redactor, but does not parse or traverse JSON payloads.

### 6. Felt.Redactor.Json — [raramer/Felt.Redactor.Json](https://github.com/raramer/Felt.Redactor.Json)
* **Package:** [Felt.Redactor.Json on NuGet](https://www.nuget.org/packages/Felt.Redactor.Json)
* **Downloads:** ~9,600
* **Approach:** Built on top of `Newtonsoft.Json` (`JObject`/`JToken` DOM), requiring legacy JSON dependencies and suffering the same DOM heap allocation issues.

### 7. SecureRedact — [sanketsingh001/SecureRedact](https://github.com/sanketsingh001/SecureRedact)
* **Package:** [SecureRedact.AspNetCore on NuGet](https://www.nuget.org/packages/SecureRedact.AspNetCore)
* **Downloads:** ~120
* **Approach:** Content-detection scanner inside `Utf8JsonReader` with AES-GCM reversible encryption tokens. Lacks property-path matching and has encryption overhead (~3,680 records/sec).

---

## Handling Incomplete & Truncated JSON Payloads

In production HTTP traffic, JSON payloads are regularly cut short due to:
* Body logging limits (e.g. ASP.NET Core `HttpLogging` default 32 KB cap or `MaxBodyBytes` limits).
* Network timeouts, abrupt connection drops, or client cancellations mid-transfer.

Consider this incomplete JSON body where closing brackets are missing:

```json
{
   "user": {
      "DriverLicense": "vvvvvvv3444"
```

How do the different libraries handle this in production?

| Library | Behavior on Incomplete JSON | Output | Security & Stability Impact |
| :--- | :--- | :--- | :--- |
| **[DragoAnt Observer 2.0](https://github.com/DragoAnt/Extensions.System.Text.Json)** | **Fail-closed streaming with container synthesis.** Masks tokens read so far, synthesizes missing closing brackets (`}}`), and returns `MaskStatus.Truncated`. | `{"user":{"DriverLicense":"***"}}` | ✅ **100% safe.** Valid JSON emitted, sensitive value masked, 0 leaks, 0 crashes. |
| **[JsonMasking](https://github.com/ThiagoBarradas/jsonmasking)** | **Throws unhandled exception.** `JsonNode.Parse` fails with `JsonReaderException: '}' expected`. | *None (throws)* | ❌ **Crash / Leak risk.** If the application falls back to logging raw input on failure, the driver's license is leaked in cleartext. |
| **[Slin.Masking](https://github.com/sw0/Slin.Masking)** | **Catches parse error and returns the raw input unchanged.** | `{"user":{"DriverLicense":"vvvvvvv3444"` | 🚨 **Severe Security Breach.** The raw unmasked sensitive value is silently logged to disk/SIEM. |
| **[MS Compliance Redaction](https://github.com/dotnet/extensions)** | Does not parse or inspect JSON bodies. | N/A | ❌ Leaves JSON body unmasked. |
| **[Json.Masker](https://github.com/myarichuk/Json.Masker)** | Requires strongly typed C# DTOs to serialize. | N/A | ❌ Cannot deserialize or mask incomplete raw text. |

### Why Container Synthesis Matters
When downstream log forwarders (e.g. Datadog, Elastic, Loki, CloudWatch) receive malformed JSON with missing closing brackets, they either reject the log line, fail structured indexing, or corrupt downstream JSON pipelines.

`DragoAnt.System.Text.Json.Observer` ensures that even when an incoming byte stream is cut short:
1. Every token up to the cutoff is evaluated against masking rules.
2. The partial JSON document is closed cleanly with the necessary `}` and `]` brackets.
3. The output is **syntactically valid JSON** containing masked values only.

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
   The companion package [DragoAnt.System.Text.Json.Observer.Http](https://github.com/DragoAnt/Extensions.System.Text.Json/tree/main/DragoAnt.System.Text.Json.Observer.Http) provides `JsonBodyLoggingHandler`, seamlessly integrating with `IHttpClientFactory` and `WithBodyLogging<TReq, TResp>()` with stream-preserving replay (`PrefixRemainderStream`).
