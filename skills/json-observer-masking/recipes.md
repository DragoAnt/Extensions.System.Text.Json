# Recipes — json-observer-masking

Task-shaped solutions. Each block is a complete program; the `// Output:` comment is what it prints.

## Log a request body safely

A body headed for a log is usually size-capped, so it may be cut mid-document. Mask with a `MaxOutputBytes` limit and log the status next to the body — the masked text is valid JSON whatever the status.

```csharp
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var logged = BodyLog.Describe("""{"user":"alice","password":"s3cret","card":{"number":"4111111111111111","cvv":123}}""");
Console.WriteLine(logged);
// Output:
// Masked {"user":"alice","password":"***","card":{"number":"***1111","cvv":"***"}}

static class BodyLog
{
    private static readonly JsonObserver Masker = JsonObserver.Obj(Relative(rules => rules
            .Match(PropMatches.OneOf("password", "secret", "cvv")).MaskAny(MaskTag.Full)
            .Match("card", "number").MaskAny(MaskTag.Last4)
            .Match(PropMatches.EndsWith("token")).MaskAny(MaskTag.Full),
        BlockList));

    private static readonly JsonObserverOptions Options = new(MaxOutputBytes: 4096, MaxValueBytes: 512);

    public static string Describe(string body)
    {
        var masked = Masker.Mask(body, out var result, Options);
        return $"{result.Status} {masked}";
    }
}
```

## Hot path: UTF-8 API

`Mask(ReadOnlySpan<byte>, IBufferWriter<byte>, JsonObserverOptions?)` reads bytes and writes bytes, with no string conversion either way. Reuse the output buffer (`ResetWrittenCount()`), and keep it per thread — an `ArrayBufferWriter<byte>` is not thread-safe, the observer is.

```csharp
using System.Buffers;
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var first = Utf8Masking.Mask("""{"user":"alice","password":"p1"}"""u8);
var second = Utf8Masking.Mask("""{"user":"bob","password":"p2"}"""u8);
Console.WriteLine(first);
Console.WriteLine(second);
// Output:
// Masked {"user":"alice","password":"***"}
// Masked {"user":"bob","password":"***"}

static class Utf8Masking
{
    private static readonly JsonObserver Masker = JsonObserver.Obj(Relative(rules => rules.Match("password").MaskAny("***"), BlockList));

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? _output;

    public static string Mask(ReadOnlySpan<byte> utf8)
    {
        var output = _output ??= new ArrayBufferWriter<byte>(4096);
        output.ResetWrittenCount();
        var result = Masker.Mask(utf8, output);
        return $"{result.Status} {Encoding.UTF8.GetString(output.WrittenSpan)}";
    }
}
```

In a real hot path, write `output.WrittenSpan` straight to the log sink instead of decoding it to a string. With constant or tag rules, a warm call allocates nothing; a rule with a masking function allocates the `string` it receives. Measure with `GC.GetAllocatedBytesForCurrentThread()` rather than assuming.

## Cut-off or invalid bodies

Both APIs never throw. Branch on `MaskResult.Status`:

| `MaskStatus` | Meaning | Output |
| --- | --- | --- |
| `Masked` | the whole payload was read and masked | the masked JSON |
| `Truncated` | the payload ended inside the document, or the output reached `MaxOutputBytes` (`FailedAtByte` = where reading stopped), or a string was cut to `MaxValueBytes` (`FailedAtByte` = -1) | valid JSON: the masked part, open objects and arrays closed |
| `Invalid` | not valid JSON (including plain text), deeper than `MaxDepth`, the root type the observer does not accept, or a rule threw | valid JSON: the masked part read before the failure (may be empty) |
| `NotJson` | empty input, or valid JSON whose root is not an object or array (`"text"`, `42`) | empty string, `BytesWritten` 0 |

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var masker = JsonObserver.Obj(Relative(rules => rules.Match("password").MaskAny("***"), BlockList));

foreach (var body in new[]
         {
             """{"user":"alice","password":"s3cret"}""",
             """{"user":{"login":"alice","password":"s3cret","roles":["admin","dev""",
             """{"user":"alice",,}""",
             "42",
             "not json",
         })
{
    var masked = masker.Mask(body, out var result);
    Console.WriteLine($"{result.Status,-9} at={result.FailedAtByte,3} {masked}".TrimEnd());
}
// Output:
// Masked    at= -1 {"user":"alice","password":"***"}
// Truncated at= 61 {"user":{"login":"alice","password":"***","roles":["admin"]}}
// Invalid   at= 16 {"user":"alice"}
// NotJson   at=  0
// Invalid   at=  0
```

A value that is cut never leaks: a string being written when the input ends is dropped, not written in part.

## Limits and output settings

`JsonObserverOptions` applies to both APIs. Build it once and reuse it.

| Option | Default | Effect |
| --- | --- | --- |
| `MaxOutputBytes` | unlimited | output cap in UTF-8 bytes; reaching it closes the output → `Truncated` |
| `MaxValueBytes` | unlimited | longest string written unmasked; a longer one is cut and ends with `…` → `Truncated`; masking functions receive the whole value and their output is never cut |
| `MaxDepth` | 64 | deeper nesting → `Invalid` |
| `RelaxedEscaping` | `true` | non-ASCII and HTML characters written unescaped |
| `HashKey` | random per process | key of `MaskTag.Hash` |
| `MaskStrategy` | built-in | writes `MaskTag` rules |
| `IgnoreNulls` | `false` | drops `null` properties and items, and objects and arrays left empty by that |
| `Indented` | `false` | indented output |

```csharp
using DragoAnt.System.Text.Json.Observer;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var masker = JsonObserver.Obj(BlockList);
const string json = """{"note":"a very long free-text note","empty":null,"tags":[null],"city":"Zürich"}""";

Console.WriteLine(masker.Mask(json, new JsonObserverOptions(MaxValueBytes: 10, IgnoreNulls: true)));
Console.WriteLine(masker.Mask(json, new JsonObserverOptions(RelaxedEscaping: false)));
// Output:
// {"note":"a very lon…","city":"Zürich"}
// {"note":"a very long free-text note","empty":null,"tags":[null],"city":"Z\u00FCrich"}
```

## Correlate masked values across services

`MaskTag.Hash` writes `hash:` and 16 hex characters of an HMAC-SHA256. Give every instance the same `HashKey` (from configuration or a secret store, never from source) and the same value hashes the same everywhere, so you can follow one customer through logs without seeing their email. Without a key, hashes only correlate inside one process.

```csharp
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var masker = JsonObserver.Obj(Relative(rules => rules.Match("email").MaskAny(MaskTag.Hash), BlockList));
var keyFromConfiguration = Encoding.UTF8.GetBytes("load-me-from-configuration");

var serviceA = new JsonObserverOptions(HashKey: keyFromConfiguration);
var serviceB = new JsonObserverOptions(HashKey: keyFromConfiguration.ToArray());

var a = masker.Mask("""{"email":"alice@example.com"}""", serviceA);
var b = masker.Mask("""{"email":"alice@example.com"}""", serviceB);
Console.WriteLine(a == b);
Console.WriteLine(a!.Contains("alice", StringComparison.Ordinal));
// Output:
// True
// False
```

## Custom mask strategy

`MaskTag` rules are written by a `Utf8MaskStrategy`. Replace it per call with `JsonObserverOptions.MaskStrategy` — one strategy serves every tag; delegate the kinds you do not change to `Utf8MaskStrategy.Default`. `value` is the unescaped string, the literal of a number or boolean, or empty for an object or array; write exactly one value.

```csharp
using System.Text.Json;
using DragoAnt.System.Text.Json.Observer;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

var masker = JsonObserver.Obj(Relative(rules => rules
        .Match("password").MaskAny(MaskTag.Full)
        .Match("card").MaskAny(MaskTag.Last4),
    BlockList));

var options = new JsonObserverOptions(MaskStrategy: new RedactedStrategy());
Console.WriteLine(masker.Mask("""{"password":"p","card":"4111111111111111"}""", options));
// Output:
// {"password":"[redacted]","card":"***1111"}

sealed class RedactedStrategy : Utf8MaskStrategy
{
    public override void Mask(ReadOnlySpan<byte> value, JsonTokenType tokenType, MaskTag tag, JsonWriter writer, JsonObserverOptions options)
    {
        if (tag.Kind == MaskKind.Full)
        {
            writer.WriteStringValue("[redacted]"u8);
            return;
        }

        Default.Mask(value, tokenType, tag, writer, options);
    }
}
```

A strategy that throws turns the result into `Invalid` with the masked prefix before that value — it never writes the value in clear.
