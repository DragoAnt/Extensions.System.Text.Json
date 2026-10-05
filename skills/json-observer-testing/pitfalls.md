# Pitfalls — json-observer-testing

False greens (a test that cannot catch the leak) and false reds (a test that fails for the wrong reason). Each block is a complete xUnit v3 test file that passes and shows the correct form.

## False green: the test rebuilds the rules

A test that declares its own `JsonObserver.Obj(...)` with "the same" rules tests that copy, not the production masker. Reference the production field or factory; if it is `internal`, use `InternalsVisibleTo` for the test project.

## False green: the secret value is too short or too common

`Assert.DoesNotContain("1", output)` fails on any other `1`; `Assert.DoesNotContain("x", output)` may pass while the real value leaks under another name. Use distinctive values (`S3cr3t-7f2a`, `4111111111111111`) that cannot occur by accident.

## False green: only the golden string is checked

A long golden literal hides a leak nobody reads. Add an explicit `DoesNotContain` per secret, and a `Contains` for a safe value so a mask-everything regression also fails.

## False green: the regression test passes before the fix

It does not reproduce the leak — usually the payload was simplified and lost the shape that defeated the rule (an array level, a number instead of a string, different casing). Rebuild it from the reported structure and see it fail first ([recipes.md](./recipes.md#a-regression-test-for-a-reported-leak)).

## False red: corruption fuzz on a block-list observer

Random corruption can change a property name; under `BlockList` the renamed property is not sensitive any more and its value is written — correctly. Fuzz corruption with an allow-list observer; fuzz block-list observers with prefixes only.

```csharp
using System.Text;
using DragoAnt.System.Text.Json.Observer;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public sealed class WhyNotBlockListCorruptionTests
{
    [Fact]
    public void RenamedProperty_EscapesABlockListRule_ButNotAnAllowList()
    {
        var corrupted = """{"pas#word":"S3cr3t-7f2a"}""";

        var blockList = JsonObserver.Obj(AnyDepth(rules => rules.Match("password").Mask("***"), BlockList));
        var allowList = JsonObserver.Obj(root => root.Match("user").Unmasked());

        Assert.Contains("S3cr3t-7f2a", blockList.Mask(corrupted));
        Assert.DoesNotContain("S3cr3t-7f2a", allowList.Mask(corrupted));
    }
}
```

## False red: expecting `Masked` for a cut or non-JSON input

A cut payload is `Truncated`; plain text is `Invalid`; a JSON scalar root (`42`, `"text"`) or empty input is `Unrecognized`; a root array given to `JsonObserver.Obj(...)` is `Invalid`.

```csharp
using DragoAnt.System.Text.Json.Observer;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public sealed class StatusExpectationTests
{
    private static readonly JsonObserver Observer = JsonObserver.Obj(BlockList);

    [Theory]
    [InlineData("""{"a":1}""", MaskStatus.Masked)]
    [InlineData("""{"a":1""", MaskStatus.Truncated)]
    [InlineData("hello", MaskStatus.Invalid)]
    [InlineData("42", MaskStatus.Unrecognized)]
    [InlineData("", MaskStatus.Unrecognized)]
    [InlineData("[1]", MaskStatus.Invalid)]
    public void Status_MatchesTheInput(string input, MaskStatus expected)
    {
        Observer.Mask(input, out var result);
        Assert.Equal(expected, result.Status);
    }
}
```

## False red: `MaskTag.Hash` output in a golden string

Without `JsonObserverOptions.HashKey`, the key is random per process, so the hash changes every run. Pass a fixed key in the test, or assert the shape (24 base64 characters, `HmacRedactor`'s format) and equality between two calls.

```csharp
using System.Text;
using System.Text.RegularExpressions;
using DragoAnt.System.Text.Json.Observer;
using Xunit;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

public sealed class HashTests
{
    private static readonly JsonObserver Observer = JsonObserver.Obj(AnyDepth(rules => rules.Match("email").Mask(MaskTag.Hash), BlockList));

    [Fact]
    public void Hash_HasAStableShape_AndCorrelates()
    {
        var options = new JsonObserverOptions { HashKey = Encoding.UTF8.GetBytes("test-key") };

        var first = Observer.Mask("""{"email":"a@b.c"}""", options);
        var second = Observer.Mask("""{"email":"a@b.c"}""", options);
        var other = Observer.Mask("""{"email":"z@b.c"}""", options);

        Assert.Matches("""^\{"email":"[A-Za-z0-9+/]{22}=="\}$""", first);
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }
}
```

## False red: an HTTP response entry read too early

The response entry is written by a background read. A test that inspects the sink right after `SendAsync` races it. Wait on the sink with a timeout ([examples.md](./examples.md#http-body-logging)).

## False red: culture-dependent numbers

A context value read with `ReadDecimal` is a `decimal`; formatting it with the current culture prints `19,90` on some machines. Compare the `decimal` itself, or format with `CultureInfo.InvariantCulture`.

## Flaky: allocation budgets measured cold or across threads

Measure on one thread with `GC.GetAllocatedBytesForCurrentThread()`, after warm-up calls, with a reused output buffer and an average over many calls. `GC.GetTotalAllocatedBytes` counts other threads (the test runner included).

## Slow: fuzz loops that are too large

Every prefix of a payload of a few hundred bytes and a few thousand seeded corruptions take well under a second. Grow the payload, not the iteration count, when you need more coverage — and keep the seed fixed so a failure is reproducible.
