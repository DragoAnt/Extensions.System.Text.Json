using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DragoAnt.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract partial class RobustnessTests
{
    private static readonly string[] MatrixObserverKeys =
    ["anydepth-blocklist", "allowlist-unmasked", "shape-maskwhole", "shape-descend", "shape-passthrough", "extraction"];

    public static TheoryData<string, string> HandRows()
    {
        var data = new TheoryData<string, string>();
        foreach (var document in MatrixCorpus.Hand.Keys)
        foreach (var observer in MatrixObserverKeys) data.Add(document, observer);
        return data;
    }

    public static TheoryData<string, string, string> CommentRows()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var document in MatrixCorpus.Hand.Values.Where(x => x.HasComments))
        foreach (var observer in new[] { "comment-keep", "comment-drop", "comment-masked-owner" })
        foreach (var policy in MatrixProfiles.CommentPolicies) data.Add(document.Name, observer, policy.Name);
        return data;
    }

    public static TheoryData<string> HandKeys()
    {
        var data = new TheoryData<string>();
        foreach (var document in MatrixCorpus.Hand.Keys) data.Add(document);
        return data;
    }

    [Fact]
    public void Matrix_Invariants_BareMaskOnTruncatedInput_IsNotALeak()
    {
        var document = new MatrixDocument("bare-mask", "{\"password\":\"ZQX1\"}"u8.ToArray(), false, false, 19);
        var output = "{\"password\":\"***\"}"u8.ToArray();
        var run = new MatrixRun(
            document, "test", "default", new(), "sequence-1-byte", 18,
            new MaskResult { Status = MaskStatus.Truncated, BytesWritten = output.Length, FailedAtByte = 18 },
            output, null, output, false);

        MatrixInvariants.Check(run).Should().NotContain(x => x.Contains("I3", StringComparison.Ordinal));
    }

    [Fact]
    public void Matrix_Invariants_DetectPlantedViolations()
    {
        var document = new MatrixDocument("planted", "{\"note\":\"hello\"}"u8.ToArray(), false, false, 16);
        var clean = new MaskResult { Status = MaskStatus.Masked, BytesWritten = 16, FailedAtByte = -1 };
        MatrixRun Run(byte[] output, MaskResult? result = null, Exception? thrown = null, byte[]? full = null, int cut = 16) =>
            new(document, "test", "default", new(), "span", cut, result ?? clean, output, thrown, full ?? "{\"note\":\"hello\"}"u8.ToArray(), false);
        MatrixRun SelfRun(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var selfDocument = new MatrixDocument("planted-self", bytes, false, false, bytes.Length);
            return new(selfDocument, "test", "default", new(), "span", bytes.Length, clean, bytes, null, bytes, false);
        }

        MatrixInvariants.Check(Run([], thrown: new InvalidOperationException())).Select(FailureId).Should().Equal("I1");
        MatrixInvariants.Check(Run("{\"a\":"u8.ToArray(), full: "{\"a\":"u8.ToArray())).Select(FailureId).Should().Equal("I2");
        MatrixInvariants.Check(SelfRun("{\"password\":\"ZQX1\"}")).Select(FailureId).Should().Equal("I3");
        MatrixInvariants.Check(SelfRun("{\"note\":\"\\u005AQX1\"}")).Select(FailureId).Should().Equal("I3");
        MatrixInvariants.Check(Run("{\"note\":\"hel\"}"u8.ToArray(), full: "{\"note\":\"hel\"}"u8.ToArray())).Select(FailureId).Should().Equal("I4");
        MatrixInvariants.Check(Run([], clean, cut: 2)).Select(FailureId).Should().Equal("I5");
        MatrixInvariants.Check(Run("{\"note\":\"hello\"}"u8.ToArray(), full: "{\"note\":\"hello2\"}"u8.ToArray())).Select(FailureId).Should().Equal("I7");
        MatrixInvariants.Check(Run([], clean with { FailedAtByte = 17 })).Select(FailureId).Should().Equal("I8");

        foreach (var json in new[]
        {
            "{\"pin\":false}",
            "{\"pin\":-0}",
            "{\"password\":\"clear\"}",
        })
        {
            var scalarDocument = new MatrixDocument("planted-sensitive-scalar", Encoding.UTF8.GetBytes(json), false, false, Encoding.UTF8.GetByteCount(json));
            var scalarRun = new MatrixRun(scalarDocument, "test", "default", new(), "span", scalarDocument.RootEnd, clean,
                scalarDocument.Utf8, null, scalarDocument.Utf8, false);
            MatrixInvariants.Check(scalarRun).Select(FailureId).Should().Equal(["I3"], json);
        }

        const string cardInput = "{\"card\":\"account-9876\"}";
        var cardDocument = new MatrixDocument("planted-card", Encoding.UTF8.GetBytes(cardInput), false, false, Encoding.UTF8.GetByteCount(cardInput));
        var cardOutput = "{\"card\":\"***1234\"}"u8.ToArray();
        var cardRun = new MatrixRun(cardDocument, "test", "default", new(), "span", cardDocument.RootEnd, clean,
            cardOutput, null, cardOutput, false);
        MatrixInvariants.Check(cardRun).Select(FailureId).Should().Equal("I3");

        const string numberInput = "{\"n\":731904731904}";
        var numberDocument = new MatrixDocument("planted-number", Encoding.UTF8.GetBytes(numberInput), false, false, Encoding.UTF8.GetByteCount(numberInput));
        var numberRun = new MatrixRun(numberDocument, "test", "default", new(), "span", numberDocument.RootEnd, clean,
            numberDocument.Utf8, null, numberDocument.Utf8, false);
        MatrixInvariants.Check(numberRun).Select(FailureId).Should().Equal("I3");

        const string containerInput = "{\"blob\":{\"password\":\"***\",\"ok\":true,\"nil\":null}}";
        var containerDocument = new MatrixDocument("planted-container", Encoding.UTF8.GetBytes(containerInput), false, false, Encoding.UTF8.GetByteCount(containerInput));
        var containerRun = new MatrixRun(containerDocument, "test", "default", new(), "span", containerDocument.RootEnd, clean,
            containerDocument.Utf8, null, containerDocument.Utf8, false);
        MatrixInvariants.Check(containerRun).Select(FailureId).Should().Equal("I3");

        var oversizedCutOutput = "{\"note\":\"hello…\"}"u8.ToArray();
        var oversizedCut = new MatrixRun(document, "test", "value-3", new() { MaxValueBytes = 3 }, "span", document.RootEnd,
            clean with { Status = MaskStatus.Truncated, Flags = MaskFlags.ValueCut }, oversizedCutOutput, null, oversizedCutOutput, false);
        MatrixInvariants.Check(oversizedCut).Select(FailureId).Should().Equal("I4");

        const string controlInput = "{\"password\":\"clear\",\"card\":\"account-9876\",\"n\":731904731904,\"safe\":0}";
        var controlDocument = new MatrixDocument("clean-sensitive-control", Encoding.UTF8.GetBytes(controlInput), false, false, Encoding.UTF8.GetByteCount(controlInput));
        var controlOutput = "{\"password\":\"***\",\"card\":\"***9876\",\"n\":0}"u8.ToArray();
        var controlRun = new MatrixRun(controlDocument, "test", "default", new(), "span", controlDocument.RootEnd, clean,
            controlOutput, null, controlOutput, false);
        MatrixInvariants.Check(controlRun).Should().BeEmpty();

        var last4Document = new MatrixDocument("planted-last4", "{\"card\":\"ZQX12345678\"}"u8.ToArray(), false, false, 22);
        var badLast4 = new MatrixRun(last4Document, "test", "default", new(), "span", 22, clean,
            "{\"card\":\"***2345678\"}"u8.ToArray(), null, "{\"card\":\"***2345678\"}"u8.ToArray(), false);
        MatrixInvariants.Check(badLast4).Select(FailureId).Should().Equal("I3");

        var cutWithoutFlag = Run("{\"note\":\"hel…\"}"u8.ToArray());
        MatrixInvariants.Check(cutWithoutFlag).Select(FailureId).Should().Contain(["I4", "I5"]);

        var correctLast4Output = "{\"card\":\"***5678\"}"u8.ToArray();
        var correctLast4 = badLast4 with { Output = correctLast4Output, FullOutput = correctLast4Output };
        MatrixInvariants.Check(correctLast4).Should().BeEmpty();
        var correctCutOutput = "{\"note\":\"hel…\"}"u8.ToArray();
        var correctCut = new MatrixRun(document, "test", "value-3", new() { MaxValueBytes = 3 }, "span", 16, clean with
            {
                Status = MaskStatus.Truncated,
                Flags = MaskFlags.ValueCut,
            }, correctCutOutput, null, correctCutOutput, false);
        MatrixInvariants.Check(correctCut).Should().BeEmpty();

        var wholeReads = new HashSet<MatrixReadValue> { new("string", "whole-value") };
        var partialRead = () => AssertWholeScalarReads([new("string", "whole")], wholeReads, "planted partial extraction");
        partialRead.Should().Throw<Exception>();

        var nonMonotone = Run("{}"u8.ToArray(), full: "{\"a\":1,\"b\":2}"u8.ToArray()) with
        {
            PreviousOutput = "{\"a\":1}"u8.ToArray(),
        };
        MatrixInvariants.Check(nonMonotone).Select(FailureId).Should().Equal("I7");

        static string FailureId(string failure) => failure.Split(':', StringSplitOptions.TrimEntries)[1];
    }

    [Theory, MemberData(nameof(HandRows))]
    public void Matrix_EveryPrefix_SpanEntry_HoldsInvariants(string document, string observer)
    {
        var failures = new List<string>();
        var source = MatrixCorpus.Hand[document];
        var instance = MatrixObservers.All[observer].Create(source.RootIsArray);
        foreach (var profile in MatrixProfiles.Options)
        {
            var full = RunSpan(instance, source.Utf8, profile.Options);
            byte[]? previous = null;
            for (var cut = 0; cut <= source.Utf8.Length; cut++)
            {
                var run = RunSpan(instance, source.Utf8.AsSpan(0, cut), profile.Options);
                failures.AddRange(MatrixInvariants.Check(new MatrixRun(source, observer, profile.Name, profile.Options, "span", cut,
                    run.Result, run.Output, run.Thrown, full.Output, VariantLeakAllowed(observer, profile.Options), previous)));
                previous = run.Output;
            }
        }
        failures.Should().BeEmpty(string.Join('\n', failures.Take(20)));
    }

    [Theory, MemberData(nameof(HandRows))]
    public void Matrix_EverySplitPoint_SequenceEqualsSpan(string document, string observer)
    {
        var source = MatrixCorpus.Hand[document];
        var instance = MatrixObservers.All[observer].Create(source.RootIsArray);
        foreach (var profile in MatrixProfiles.Options)
        {
            var fullSpan = RunSpan(instance, source.Utf8, profile.Options);
            for (var split = 0; split <= source.Utf8.Length; split++)
            {
                var splitOutput = new ArrayBufferWriter<byte>();
                var splitResult = instance.Mask(SequenceInputTests.SplitAt(source.Utf8, split), splitOutput, profile.Options);
                splitResult.Should().Be(fullSpan.Result, $"{document}/{observer}/{profile.Name}/split={split}");
                splitOutput.WrittenSpan.ToArray().Should().Equal(fullSpan.Output);
            }

        }
    }

    [Theory, MemberData(nameof(HandRows))]
    public void Matrix_EveryPrefix_OneByteSequenceEqualsSpan(string document, string observer)
    {
        var source = MatrixCorpus.Hand[document];
        var instance = MatrixObservers.All[observer].Create(source.RootIsArray);
        foreach (var profile in MatrixProfiles.Options)
        {
            for (var cut = 0; cut <= source.Utf8.Length; cut++)
            {
                var prefix = source.Utf8.AsSpan(0, cut).ToArray();
                var span = RunSpan(instance, prefix, profile.Options);
                var sequence = RunSequence(instance, SequenceInputTests.Split(prefix, 1), profile.Options);
                sequence.Result.Should().Be(span.Result, $"{document}/{observer}/{profile.Name}/cut={cut}");
                sequence.Output.Should().Equal(span.Output);
            }
        }
    }

    [Theory, MemberData(nameof(HandRows))]
    public void Matrix_EveryPrefix_StringAndCharEntries_EqualSpan(string document, string observer)
    {
        var source = MatrixCorpus.Hand[document];
        var instance = MatrixObservers.All[observer].Create(source.RootIsArray);
        foreach (var profile in MatrixProfiles.Options.Where(x => x.Name is "default" or "nulls+indented" or "case-sensitive"))
        for (var cut = 0; cut <= source.Utf8.Length; cut++)
        {
            var prefix = source.Utf8.AsSpan(0, cut);
            if (!global::System.Text.Unicode.Utf8.IsValid(prefix)) continue;
            var text = Encoding.UTF8.GetString(prefix);
            var span = RunSpan(instance, prefix, profile.Options);
            var stringOutput = instance.Mask(text, out var stringResult, profile.Options) ?? string.Empty;
            var charBytes = new ArrayBufferWriter<byte>();
            var charByteResult = instance.Mask(text.AsSpan(), charBytes, profile.Options);
            var chars = new ArrayBufferWriter<char>();
            var charResult = instance.Mask(text.AsSpan(), chars, profile.Options);

            Encoding.UTF8.GetBytes(stringOutput).Should().Equal(span.Output, $"{document}/{observer}/{profile.Name}/cut={cut}/string");
            charBytes.WrittenSpan.ToArray().Should().Equal(span.Output, $"{document}/{observer}/{profile.Name}/cut={cut}/char-bytes");
            Encoding.UTF8.GetBytes(new string(chars.WrittenSpan)).Should().Equal(span.Output, $"{document}/{observer}/{profile.Name}/cut={cut}/chars");
            const string resultShapeReason = "string and char entries compare status and flags only because UTF-16 and UTF-8 byte counts differ by design";
            stringResult.Status.Should().Be(span.Result.Status, resultShapeReason);
            stringResult.Flags.Should().Be(span.Result.Flags, resultShapeReason);
            charByteResult.Status.Should().Be(span.Result.Status, resultShapeReason);
            charByteResult.Flags.Should().Be(span.Result.Flags, resultShapeReason);
            charResult.Status.Should().Be(span.Result.Status, resultShapeReason);
            charResult.Flags.Should().Be(span.Result.Flags, resultShapeReason);
        }
    }

    [Theory, MemberData(nameof(CommentRows))]
    public void Matrix_CommentPolicyAndRule_EveryPrefix_NeverLeaks(string document, string observer, string policy)
    {
        var source = MatrixCorpus.Hand[document];
        var instance = MatrixObservers.All[observer].Create(source.RootIsArray);
        var commentPolicy = MatrixProfiles.CommentPolicies.Single(x => x.Name == policy).Policy;
        var options = new JsonObserverOptions { Comments = commentPolicy };
        var full = RunSpan(instance, source.Utf8, options);
        var failures = new List<string>();
        byte[]? previous = null;
        for (var cut = 0; cut <= source.Utf8.Length; cut++)
        {
            var run = RunSpan(instance, source.Utf8.AsSpan(0, cut), options);
            failures.AddRange(MatrixInvariants.Check(new(source, observer, policy, options, "span", cut, run.Result, run.Output, run.Thrown, full.Output, false, previous)));
            previous = run.Output;
        }
        failures.Should().BeEmpty(string.Join('\n', failures.Take(20)));
    }

    [Theory]
    [InlineData("anydepth-blocklist")]
    [InlineData("allowlist-unmasked")]
    [InlineData("shape-maskwhole")]
    [InlineData("shape-descend")]
    [InlineData("shape-passthrough")]
    public void Matrix_GeneratedDocuments_EveryPrefix_HoldInvariants(string observer)
    {
        var failures = new List<string>();
        foreach (var source in MatrixCorpus.Generated)
        foreach (var profile in MatrixProfiles.Options.Where(x => x.Name is "default" or "ignore-nulls" or "out-40+indented" or "case-sensitive"))
        {
            var instance = MatrixObservers.All[observer].Create(source.RootIsArray);
            var full = RunSpan(instance, source.Utf8, profile.Options);
            byte[]? previous = null;
            for (var cut = 0; cut <= source.Utf8.Length; cut++)
            {
                var run = RunSpan(instance, source.Utf8.AsSpan(0, cut), profile.Options);
                failures.AddRange(MatrixInvariants.Check(new(source, observer, profile.Name, profile.Options, "span", cut, run.Result, run.Output, run.Thrown, full.Output, VariantLeakAllowed(observer, profile.Options), previous)));
                previous = run.Output;
            }
        }
        failures.Should().BeEmpty(string.Join('\n', failures.Take(20)));
    }

    [Theory]
    [InlineData("anydepth-blocklist")]
    [InlineData("allowlist-unmasked")]
    [InlineData("shape-maskwhole")]
    [InlineData("shape-descend")]
    [InlineData("shape-passthrough")]
    public void Matrix_GeneratedDocuments_EverySeventhPrefix_SequenceEqualsSpan(string observer)
    {
        foreach (var source in MatrixCorpus.Generated)
        foreach (var profile in MatrixProfiles.Options.Where(x => x.Name is "default" or "ignore-nulls" or "out-40+indented" or "case-sensitive"))
        {
            var instance = MatrixObservers.All[observer].Create(source.RootIsArray);
            for (var cut = 0; cut <= source.Utf8.Length; cut += 7)
            {
                var prefix = source.Utf8.AsSpan(0, cut).ToArray();
                var span = RunSpan(instance, prefix, profile.Options);
                var sequence = RunSequence(instance, SequenceInputTests.Split(prefix, 1), profile.Options);
                sequence.Result.Should().Be(span.Result, $"{source.Name}/{observer}/{profile.Name}/cut={cut}");
                sequence.Output.Should().Equal(span.Output);
            }
        }
    }

    [Theory, MemberData(nameof(HandKeys))]
    public void Matrix_Extraction_ReadsWholeInputScalarsOnly(string document)
    {
        var source = MatrixCorpus.Hand[document];
        var observer = MatrixObservers.CreateExtraction();
        var wholeScalars = WholeScalarReads(source.Utf8);
        foreach (var options in new[] { new JsonObserverOptions(), new JsonObserverOptions { NameCaseInsensitive = false } })
        {
            var expected = ExpectedExtractionReads(source.Utf8, options.NameCaseInsensitive);
            expected.Should().NotBeEmpty(document);
            for (var cut = 0; cut <= source.Utf8.Length; cut++)
            {
                var prefix = source.Utf8.AsSpan(0, cut).ToArray();
                var fromSpan = new MatrixExtracted();
                var fromSequence = new MatrixExtracted();
                var spanResult = observer.Read(prefix, fromSpan, options);
                var sequenceResult = observer.Read(SequenceInputTests.Split(prefix, 1), fromSequence, options);
                sequenceResult.Should().Be(spanResult, $"{document}/cut={cut}");
                fromSequence.Values.Should().Equal(fromSpan.Values, $"{document}/cut={cut}");
                AssertWholeScalarReads(fromSpan.Values, wholeScalars, $"{document}/cut={cut}/span");
                AssertWholeScalarReads(fromSequence.Values, wholeScalars, $"{document}/cut={cut}/sequence");
                if (cut == source.Utf8.Length)
                {
                    fromSpan.Values.Should().BeEquivalentTo(expected, $"{document}/full/span");
                    fromSequence.Values.Should().BeEquivalentTo(expected, $"{document}/full/sequence");
                }
            }
        }
    }

    [Fact]
    public void Matrix_ShapeUnknownPolicies_RenderExtraDifferently()
    {
        var source = MatrixCorpus.Generated.First(x => !x.RootIsArray && !x.HasComments);
        var input = Encoding.UTF8.GetString(source.Utf8);
        var maskWholeExtra = Extra(MatrixObservers.All["shape-maskwhole"].Create(false).Mask(input));

        maskWholeExtra.ValueKind.Should().Be(JsonValueKind.String);
        maskWholeExtra.GetString().Should().Be("***");
        Extra(MatrixObservers.All["shape-descend"].Create(false).Mask(input)).GetProperty("note").GetString().Should().Be("***");
        Extra(MatrixObservers.All["shape-passthrough"].Create(false).Mask(input)).GetProperty("note").GetString()
            .Should().Be(MatrixCorpus.ExtraSecretMarker + "-extra");

        static JsonElement Extra(string? json)
        {
            using var document = JsonDocument.Parse(json!);
            return document.RootElement.GetProperty("extra").Clone();
        }
    }

    [Fact]
    public void Matrix_Observers_MaskTheirSensitiveSet()
    {
        var document = MatrixCorpus.Hand["upper-cased"];
        foreach (var (name, observer) in MatrixObservers.All.Where(x => !x.Key.StartsWith("comment-", StringComparison.Ordinal)))
        {
            observer.Create(document.RootIsArray).Mask(Encoding.UTF8.GetString(document.Utf8))
                .Should().NotContain(MatrixCorpus.SecretMarker, name);
        }
    }

    [Fact]
    public void Matrix_Corpus_IsValidAndMarkersOnlyUnderSensitiveNames()
    {
        foreach (var document in MatrixCorpus.Hand.Values.Concat(MatrixCorpus.Generated))
        {
            document.RootEnd.Should().BeGreaterThan(0, document.Name);
            using var parsed = JsonDocument.Parse(document.Utf8, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                MaxDepth = 128,
            });
            AssertMarkerPlacement(parsed.RootElement, document.Name);
            if (!document.HasComments)
            {
                Encoding.UTF8.GetString(document.Utf8).Should().NotContain(MatrixCorpus.CommentSecretMarker, document.Name);
            }
        }
        MatrixCorpus.Generated.Should().OnlyContain(document => Encoding.UTF8.GetString(document.Utf8).Contains("😀", StringComparison.Ordinal));
    }

    [Fact]
    public void Matrix_CorpusWalk_DetectsMisplacedMarkers()
    {
        using var secret = JsonDocument.Parse("""{"note":"ZQX-misplaced"}""");
        using var comment = JsonDocument.Parse("""{"note":"ZQC-in-value"}""");

        var misplacedSecret = () => AssertMarkerPlacement(secret.RootElement, "planted-secret");
        var commentInValue = () => AssertMarkerPlacement(comment.RootElement, "planted-comment");
        misplacedSecret.Should().Throw<Exception>();
        commentInValue.Should().Throw<Exception>();
    }

    [Fact]
    public void Matrix_Generator_IsDeterministic()
    {
        var first = MatrixCorpus.Generate(MatrixCorpus.GeneratorSeed, MatrixCorpus.GeneratedCount);
        var second = MatrixCorpus.Generate(MatrixCorpus.GeneratorSeed, MatrixCorpus.GeneratedCount);
        first.Select(x => x.Utf8).Should().BeEquivalentTo(second.Select(x => x.Utf8), o => o.WithStrictOrdering());
        var corpusHash = Convert.ToHexString(SHA256.HashData(first.SelectMany(x => x.Utf8).ToArray()));
        corpusHash.Should().Be("ACD6BA36D03839D22B03FB7507316D47212D891E50BDCB18EF3AAA2FBBCD1852");
    }

    private static (MaskResult Result, byte[] Output, Exception? Thrown) RunSpan(MatrixObserverInstance observer, ReadOnlySpan<byte> input, JsonObserverOptions options)
    {
        var output = new ArrayBufferWriter<byte>();
        try
        {
            return (observer.Mask(input, output, options), output.WrittenSpan.ToArray(), null);
        }
        catch (Exception exception)
        {
            return (default, output.WrittenSpan.ToArray(), exception);
        }
    }

    private static (MaskResult Result, byte[] Output, Exception? Thrown) RunSequence(MatrixObserverInstance observer, in ReadOnlySequence<byte> input, JsonObserverOptions options)
    {
        var output = new ArrayBufferWriter<byte>();
        try
        {
            return (observer.Mask(input, output, options), output.WrittenSpan.ToArray(), null);
        }
        catch (Exception exception)
        {
            return (default, output.WrittenSpan.ToArray(), exception);
        }
    }

    private static bool VariantLeakAllowed(string observer, JsonObserverOptions options) =>
        !options.NameCaseInsensitive && (observer is "anydepth-blocklist" or "extraction" or "shape-passthrough" || observer.StartsWith("comment-", StringComparison.Ordinal));

    private static HashSet<MatrixReadValue> WholeScalarReads(byte[] utf8)
    {
        using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 128 });
        var values = new HashSet<MatrixReadValue>();
        Visit(document.RootElement);
        return values;

        void Visit(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject()) Visit(property.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Visit(item);
                    break;
                case JsonValueKind.String:
                    values.Add(new("string", element.GetString()!));
                    break;
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out var integer)) values.Add(new("integer", integer.ToString(global::System.Globalization.CultureInfo.InvariantCulture)));
                    if (element.TryGetDecimal(out var decimalValue)) values.Add(new("decimal", decimalValue.ToString(global::System.Globalization.CultureInfo.InvariantCulture)));
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    values.Add(new("boolean", element.GetBoolean().ToString().ToLowerInvariant()));
                    break;
                case JsonValueKind.Null:
                    values.Add(new("string", "null"));
                    values.Add(new("integer", "null"));
                    values.Add(new("decimal", "null"));
                    values.Add(new("boolean", "null"));
                    break;
            }
        }
    }

    private static List<MatrixReadValue> ExpectedExtractionReads(byte[] utf8, bool caseInsensitive)
    {
        using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 128 });
        var values = new List<MatrixReadValue>();
        Visit(document.RootElement);
        return values;

        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) Visit(item);
                return;
            }
            if (element.ValueKind != JsonValueKind.Object) return;
            foreach (var property in element.EnumerateObject())
            {
                var comparison = caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                var sensitive = MatrixCorpus.SensitiveNames.Any(name => string.Equals(name, property.Name, comparison))
                    && !string.Equals(property.Name, "blob", comparison);
                if (property.Value.ValueKind == JsonValueKind.String
                    && (string.Equals(property.Name, "name", comparison) || string.Equals(property.Name, "note", comparison) || sensitive))
                    values.Add(new("string", property.Value.GetString()!));
                else if (string.Equals(property.Name, "id", comparison) && property.Value.TryGetInt64(out var integer))
                    values.Add(new("integer", integer.ToString(global::System.Globalization.CultureInfo.InvariantCulture)));
                else if (string.Equals(property.Name, "n", comparison) && property.Value.TryGetDecimal(out var decimalValue))
                    values.Add(new("decimal", decimalValue.ToString(global::System.Globalization.CultureInfo.InvariantCulture)));
                else if (string.Equals(property.Name, "ok", comparison) && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    values.Add(new("boolean", property.Value.GetBoolean().ToString().ToLowerInvariant()));
                Visit(property.Value);
            }
        }
    }

    private static void AssertWholeScalarReads(IEnumerable<MatrixReadValue> reads, IReadOnlySet<MatrixReadValue> wholeScalars, string because) =>
        reads.Should().OnlyContain(value => wholeScalars.Contains(value), because);

    private static void AssertMarkerPlacement(JsonElement element, string document, bool sensitiveAncestor = false, bool extraAncestor = false)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) AssertMarkerPlacement(item, document, sensitiveAncestor, extraAncestor);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in element.EnumerateObject())
        {
            var canonical = MatrixCorpus.SensitiveNames.Contains(property.Name);
            var extra = property.Name == "extra";
            var variant = !canonical && MatrixCorpus.SensitiveNames.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase));
            var raw = property.Value.GetRawText();
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString()!;
                if (value.Contains(MatrixCorpus.SecretMarker, StringComparison.Ordinal))
                    (canonical || sensitiveAncestor).Should().BeTrue($"{document}/{property.Name} owns a canonical marker");
                if (value.Contains(MatrixCorpus.VariantSecretMarker, StringComparison.Ordinal)) variant.Should().BeTrue($"{document}/{property.Name} owns a case-variant marker");
                value.Should().NotContain(MatrixCorpus.CommentSecretMarker, $"{document}/{property.Name} cannot carry a comment marker in a string value");
                if (value.Contains(MatrixCorpus.ExtraSecretMarker, StringComparison.Ordinal))
                    (extra || extraAncestor).Should().BeTrue($"{document}/{property.Name} owns an unknown-member marker");
            }
            if (property.Value.ValueKind == JsonValueKind.Number && raw.Contains(MatrixCorpus.NumberSecretMarker, StringComparison.Ordinal))
                canonical.Should().BeTrue($"{document}/{property.Name} owns a numeric marker");
            AssertMarkerPlacement(property.Value, document, sensitiveAncestor || canonical, extraAncestor || extra);
        }
    }
}
