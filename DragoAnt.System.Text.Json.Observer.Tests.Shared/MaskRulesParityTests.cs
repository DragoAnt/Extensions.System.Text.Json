using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using static DragoAnt.Observer.ValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class MaskRulesParityTests
{
    private static readonly NameMatch AnyItem = new(name => name is null);
    private static readonly ValuePolicy[] Policies = [BlockList, AllowList, NullList, Tagged(MaskTag.Hash)];
    private static readonly JsonObserverOptions[] Profiles =
    [
        JsonObserverOptions.Default,
        new() { NameCaseInsensitive = false },
        new() { Comments = CommentPolicy.BlockList },
        new() { IgnoreNulls = true },
        new JsonObserverOptions { HashKeyId = 3 }.WithBase64HashKey("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw=="),
        new() { MaxOutputBytes = 40 },
    ];

    private static readonly IReadOnlyDictionary<string, Pair> Pairs = new Dictionary<string, Pair>
    {
        ["match-tags"] = new(
            u => MaskRules.Create(b => b.Match("password").Mask(MaskTag.Full).Match("email").Mask(MaskTag.Hash)
                .Match(Names.EndsWith("token")).Mask(MaskTag.Last4), u),
            u => JsonObserver.Any(_ => { }, _ => { }, JsonValuePolicy.AnyDepth(b => b.Match("password").Mask(MaskTag.Full)
                .Match("email").Mask(MaskTag.Hash).Match(Names.EndsWith("token")).Mask(MaskTag.Last4), u))),
        ["path-tags"] = new(
            u => MaskRules.Create(b => b.Path("card", "number").Mask(MaskTag.Last4).Path("user", "ssn").Mask(MaskTag.Null), u),
            u => JsonObserver.Any(o => o.Path("card", "number").Mask(MaskTag.Last4).Path("user", "ssn").Mask(MaskTag.Null), _ => { }, u)),
        ["path-beats-match"] = new(
            u => MaskRules.Create(b => b.Match("number").Unmasked().Path("card", "number").Mask(MaskTag.Full), u),
            u => JsonObserver.Any(o => o.Path("card", "number").Mask(MaskTag.Full), _ => { },
                JsonValuePolicy.AnyDepth(b => b.Match("number").Unmasked(), u))),
        ["unmasked"] = new(
            u => MaskRules.Create(b => b.Match("id").Unmasked().Path("meta", "version").Unmasked(), u),
            u => JsonObserver.Any(o => o.Path("meta", "version").Unmasked(), _ => { },
                JsonValuePolicy.AnyDepth(b => b.Match("id").Unmasked(), u))),
        ["comments"] = new(
            u => MaskRules.Create(b => b.Match("password").Comment(CommentKind.Any, CommentRules.Drop).Mask(MaskTag.Full)
                .Path("timeout").Comment(CommentKind.Inline, CommentRules.Keep).Unmasked(), u),
            u => JsonObserver.Any(o => o.Path("timeout").Unmasked().Comment(CommentKind.Inline, CommentRules.Keep), _ => { },
                JsonValuePolicy.AnyDepth(b => b.Match("password").Mask(MaskTag.Full).Comment(CommentKind.Any, CommentRules.Drop), u))),
        ["items-custom"] = new(
            u => MaskRules.Create(b => b.Path("lines", AnyItem, "qty").Mask(MaskTag.Full)
                .Match(Names.OneOf("sku", "exp")).Mask(MaskTag.Custom("k")), u),
            u => JsonObserver.Any(o => o.Path("lines", AnyItem, "qty").Mask(MaskTag.Full), _ => { },
                JsonValuePolicy.AnyDepth(b => b.Match(Names.OneOf("sku", "exp")).Mask(MaskTag.Custom("k")), u))),
        ["empty"] = new(u => MaskRules.Create(_ => { }, u), u => JsonObserver.Any(_ => { }, _ => { }, u)),
    };

    public static TheoryData<string> PairNames() => new(Pairs.Keys.ToArray());

    [Theory]
    [MemberData(nameof(PairNames))]
    public void FromRules_WritesWhatTheBuilderWrites(string pairName)
    {
        var pair = Pairs[pairName];
        var mismatches = new List<string>();
        foreach (var policy in Policies)
        foreach (var profile in Profiles)
        {
            var actual = JsonObserver.From(pair.Rules(policy));
            var expected = pair.Reference(policy);
            for (var documentIndex = 0; documentIndex < ParityCorpus.Documents.Count; documentIndex++)
            {
                var document = ParityCorpus.Documents[documentIndex];
                for (var cut = 0; cut <= document.Length; cut++)
                    CompareText(actual, expected, document.AsSpan(0, cut), profile, pairName, policy, documentIndex, cut, mismatches);

                var utf8 = Encoding.UTF8.GetBytes(document);
                for (var cut = 0; cut <= utf8.Length; cut++)
                    CompareBytes(actual, expected, utf8.AsSpan(0, cut), profile, pairName, policy, documentIndex, cut, mismatches);
            }
        }

        mismatches.Should().BeEmpty(string.Join(Environment.NewLine, mismatches.Take(20)));
    }

    [Fact]
    public void FromRules_ExplainsLikeTheBuilder()
    {
        string[] names = ["match-tags", "path-tags", "path-beats-match"];
        string[] paths = ["password", "card.number", "a.b.c.card.number", "user.ssn", "other.user.ssn", "lines[0].qty", "[0].password"];
        ValueKind[] kinds = [ValueKind.String, ValueKind.Number, ValueKind.Object];
        foreach (var name in names)
        foreach (var path in paths)
        foreach (var kind in kinds)
        {
            var pair = Pairs[name];
            JsonObserver.From(pair.Rules(BlockList)).Explain(path, kind)
                .Should().BeEquivalentTo(pair.Reference(BlockList).Explain(path, kind), $"{name} {path} {kind}");
        }
    }

    [Fact]
    public void FromRules_PathBeatsMatch_AndRootArrayUsesMatches()
    {
        var observer = JsonObserver.From(Pairs["path-beats-match"].Rules(BlockList));
        observer.Mask("""{"card":{"number":"secret"},"other":{"number":"visible"}}""")
            .Should().Be("""{"card":{"number":"***"},"other":{"number":"visible"}}""");
        observer.Mask("""[{"number":"visible"}]""").Should().Be("""[{"number":"visible"}]""");
    }

    [Fact]
    public void FromRules_IsMarkedExperimental()
    {
        var marker = typeof(JsonObserver).GetMethod(nameof(JsonObserver.From))!.GetCustomAttribute<ExperimentalAttribute>()!;
        marker.DiagnosticId.Should().Be("DAOEXP001");
        marker.UrlFormat.Should().Be("https://github.com/DragoAnt/Observer/blob/main/docs/experimental.md");
    }

    [Fact]
    public void FromRules_NullRules_Throws()
        => FluentActions.Invoking(() => JsonObserver.From(null!)).Should().Throw<ArgumentNullException>();

    private static void CompareText(JsonObserver actual, JsonObserver expected, ReadOnlySpan<char> input,
        JsonObserverOptions profile, string pair, ValuePolicy policy, int document, int cut, List<string> mismatches)
    {
        var actualOutput = new ArrayBufferWriter<char>();
        var expectedOutput = new ArrayBufferWriter<char>();
        var actualResult = actual.Mask(input, actualOutput, profile);
        var expectedResult = expected.Mask(input, expectedOutput, profile);
        if (actualResult != expectedResult || !actualOutput.WrittenSpan.SequenceEqual(expectedOutput.WrittenSpan))
            mismatches.Add($"{pair}/{policy}/profile={Array.IndexOf(Profiles, profile)}/doc={document + 1}/cut={cut}/text: {actualResult} != {expectedResult}; {actualOutput.WrittenSpan.ToString()} != {expectedOutput.WrittenSpan.ToString()}");
    }

    private static void CompareBytes(JsonObserver actual, JsonObserver expected, ReadOnlySpan<byte> input,
        JsonObserverOptions profile, string pair, ValuePolicy policy, int document, int cut, List<string> mismatches)
    {
        var actualOutput = new ArrayBufferWriter<byte>();
        var expectedOutput = new ArrayBufferWriter<byte>();
        var actualResult = actual.Mask(input, actualOutput, profile);
        var expectedResult = expected.Mask(input, expectedOutput, profile);
        if (actualResult != expectedResult || !actualOutput.WrittenSpan.SequenceEqual(expectedOutput.WrittenSpan))
            mismatches.Add($"{pair}/{policy}/profile={Array.IndexOf(Profiles, profile)}/doc={document + 1}/cut={cut}/bytes: {actualResult} != {expectedResult}; {Encoding.UTF8.GetString(actualOutput.WrittenSpan)} != {Encoding.UTF8.GetString(expectedOutput.WrittenSpan)}");
    }

    private sealed record Pair(Func<ValuePolicy, MaskRules> Rules, Func<ValuePolicy, JsonObserver> Reference);
}
