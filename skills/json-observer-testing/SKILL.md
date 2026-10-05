---
name: json-observer-testing
description: Write xUnit tests that prove JSON masking built with DragoAnt.System.Text.Json.Observer never leaks a secret — golden input-to-output cases, "the secret never appears" assertions across payload shapes, truncation and byte-corruption fuzzing of the never-throw APIs (output must stay valid JSON with no secret), MaskResult/MaskStatus checks, a load-bearing check that fails when a rule is removed, allocation budgets with GC.GetAllocatedBytesForCurrentThread, thread-safety checks on a shared JsonObserver, and HttpClient body-logging tests for JsonBodyLoggingHandler with a fake inner handler and a capturing IJsonBodyLogSink. Use when adding unit or regression tests for masking rules, a JsonObserver configuration, an IJsonBodyMaskerProvider or a body-logging setup, reproducing a secret that leaked into logs, or when asked "how do I prove nothing sensitive gets logged".
---

# Testing masking with DragoAnt.System.Text.Json.Observer

A masking rule is a security control, so its tests must show that **the secret is absent**, not only that the output looks right. Seven kinds of test cover it; each has a complete xUnit v3 example in [examples.md](./examples.md), using plain `Assert`.

Packages: `DragoAnt.System.Text.Json.Observer` (and `.Http` for handler tests), `xunit.v3`. Keep the observer under test in the production code (a `static readonly` field or a factory method) and call **that** from the test — a test that rebuilds the rules by hand tests its own copy.

## Which test, when

| Test | Proves | Use it for |
| --- | --- | --- |
| [Golden](./examples.md#golden-cases) | exact masked output for given input | every rule set; documents the intent |
| [Secret absent](./examples.md#the-secret-never-appears) | no secret value appears, across payload shapes (nested, arrays, numbers, casing) | every sensitive field; regression tests for a leak |
| [Load-bearing rule](./examples.md#the-rule-is-load-bearing) | the test fails when the rule is removed | proving a secret-absent test can fail at all |
| [Truncation fuzz](./examples.md#truncation-fuzz) | every prefix of a payload stays valid JSON with no secret | bodies logged with a size cap or cut streams |
| [Corruption fuzz](./examples.md#corruption-fuzz) | random byte damage never throws or leaks | untrusted or binary-unsafe input |
| [Allocation budget](./examples.md#allocation-budget) | per-call allocations stay under a measured budget | hot-path maskers |
| [Concurrency](./examples.md#concurrency) | a shared observer gives the same output on many threads | observers in `static` fields or singletons |
| [HTTP logging](./examples.md#http-body-logging) | `JsonBodyLoggingHandler` logs masked bodies and the caller still gets the full response | `IJsonBodyMaskerProvider` and handler setups |

## Rules that matter

1. **Assert absence, not just shape.** `Assert.DoesNotContain(secret, output)` for every secret value, plus a golden check of the exact output. A golden string alone hides a leak inside a long literal nobody reads.
2. **Use distinctive secret values** (`S3cr3t-7f2a`), never `"x"` or `"1"` — a short value appears by accident elsewhere and makes absence assertions meaningless.
3. **Cover the shapes that defeat rules:** the field nested deeper, inside an array, as a number or boolean, as an object, with different casing, cut off mid-value. Each is a separate case.
4. **Prove the test can fail.** Remove the rule (or build the observer without it) and the test must go red; a check that passes either way protects nothing. Run this once by hand when writing the test, or keep it as a test ([examples.md#the-rule-is-load-bearing](./examples.md#the-rule-is-load-bearing)).
5. **Fuzz with an allow-list observer.** Random corruption can rename a property (`password` → `pas#word`); under `BlockList` that value is then legitimately unmasked, so a block-list fuzz fails for the wrong reason. Under an allow-list, unknown names are masked, so "no secret" must always hold. Truncation fuzz (cutting, not changing bytes) is safe with any observer.
6. **When output is non-empty it must parse.** For every status, `BytesWritten > 0` means the output is valid JSON; `Unrecognized` means empty output.
7. **Set allocation budgets from measurements**, with warm-up calls first and a reused output buffer, on the UTF-8 API. Never assert zero.
8. **HTTP response entries are written asynchronously.** Wait on the sink with a timeout; never read it straight after `SendAsync`.

## Pitfalls (details in [pitfalls.md](./pitfalls.md))

- A test that passes before the fix — the regression test does not reproduce the leak; write it red first.
- Asserting `Masked` on a payload you cut on purpose (it is `Truncated`), or on plain text (it is `Invalid`).
- Comparing output that contains `MaskTag.Hash` without a fixed `HashKey` — the default key is random per process.
- Culture-dependent assertions on numbers read with `ReadDecimal` — format with the invariant culture.

## Companions

- [examples.md](./examples.md) — one complete xUnit test class per test kind.
- [recipes.md](./recipes.md) — a reusable fuzz helper, a regression test for a reported leak, testing a masker provider, payload builders.
- [pitfalls.md](./pitfalls.md) — false greens and false reds, and why.

Every C# block in these files is a complete xUnit v3 test class (implicit usings) that compiles against the library and passes.
