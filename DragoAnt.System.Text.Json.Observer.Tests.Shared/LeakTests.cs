using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using DragoAnt.System.Text.Json.Observer.Strategies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class LeakTests
{
    private const string Ssn = """{"ssn":"123-45-6789","x":"y"}""";

    private static JsonObserverValueDelegate<Holder> AllowList => JsonObserverValuePolicies<Holder>.AllowList;
    private static JsonObserverValueDelegate<Holder> BlockList => JsonObserverValuePolicies<Holder>.BlockList;
    private static JsonObserverValueDelegate<Holder> NullList => JsonObserverValuePolicies<Holder>.NullList;

    [Fact]
    public void Read_UnderAllowList_WritesTheValueMasked()
    {
        var holder = new Holder();

        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep), AllowList).Mask(Ssn, holder)
            .Should().Be("""{"ssn":"***","x":"***"}""");
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Read_UnderBlockList_WritesTheValueUnchanged()
    {
        var holder = new Holder();

        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep), BlockList).Mask(Ssn, holder)
            .Should().Be(Ssn);
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Read_UnderNullList_WritesNull()
    {
        var holder = new Holder();

        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep), NullList).Mask(Ssn, holder)
            .Should().Be("""{"ssn":null,"x":null}""");
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Read_InRelativePolicy_UnderAllowList_WritesTheValueMasked()
    {
        var holder = new Holder();
        var observer = JsonObserver.Obj<Holder>(JsonObserverValuePolicies<Holder>.Relative(b => b.Match("ssn").ReadStr(Keep)));

        observer.Mask("""{"person":{"ssn":"123-45-6789"}}""", holder).Should().Be("""{"person":{"ssn":"***"}}""");
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void ReadNumber_UnderAllowList_WritesTheValueMasked()
    {
        var holder = new Holder();

        JsonObserver.Obj<Holder>(r => r.Match("pin").ReadInt((v, h) => h.Number = v)).Mask("""{"pin":1234}""", holder)
            .Should().Be("""{"pin":"***"}""");
        holder.Number.Should().Be(1234);
    }

    [Fact]
    public void Read_ThenUnmasked_WritesClearText()
    {
        var holder = new Holder();

        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep).Unmasked(), AllowList).Mask(Ssn, holder)
            .Should().Be("""{"ssn":"123-45-6789","x":"***"}""");
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Read_ThenMask_OnOneMatch_ReadsAndMasks()
    {
        var holder = new Holder();

        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep).MaskAny(MaskTag.Last4), BlockList).Mask(Ssn, holder)
            .Should().Be("""{"ssn":"***6789","x":"y"}""");
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Mask_ThenRead_OnOneMatch_ReadsAndMasks()
    {
        var holder = new Holder();

        JsonObserver.Obj<Holder>(r => r.Match("ssn").MaskAny(MaskTag.Full).Match("ssn").ReadStr(Keep), BlockList).Mask(Ssn, holder)
            .Should().Be("""{"ssn":"***","x":"y"}""");
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Read_ThenMask_InRelativePolicy_ReadsAndMasks()
    {
        var holder = new Holder();
        var observer = JsonObserver.Obj<Holder>(JsonObserverValuePolicies<Holder>.Relative(
            b => b.Match("ssn").ReadStr(Keep).MaskAny(MaskTag.Full), BlockList));

        observer.Mask("""{"person":{"ssn":"123-45-6789","x":"y"}}""", holder).Should().Be("""{"person":{"ssn":"***","x":"y"}}""");
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void ArrayRead_UnderAllowList_WritesTheItemMasked()
    {
        var holder = new Holder();

        JsonObserver.Array<Holder>(a => a.ReadStr(Keep)).Mask("""["123-45-6789"]""", holder).Should().Be("""["***"]""");
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void ArrayRead_ThenUnmaskedOrMask_DecidesTheItem()
    {
        var unmasked = new Holder();
        var masked = new Holder();

        JsonObserver.Array<Holder>(a => a.ReadStr(Keep).Unmasked()).Mask("""["123-45-6789"]""", unmasked).Should().Be("""["123-45-6789"]""");
        JsonObserver.Array<Holder>(a => a.MaskAny(MaskTag.Last4).ReadStr(Keep), BlockList).Mask("""["123-45-6789"]""", masked)
            .Should().Be("""["***6789"]""");
        unmasked.Value.Should().Be("123-45-6789");
        masked.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Read_ExtractionOnly_StillReads()
    {
        var holder = new Holder();

        JsonObserver.Obj<Holder>(r => r.Match("ssn").MaskAny(MaskTag.Full).Match("ssn").ReadStr(Keep)).Read(Ssn, holder)
            .Status.Should().Be(MaskStatus.Masked);
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Explain_Read_ReportsWhatIsWritten()
    {
        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep), AllowList).Explain("ssn").Outcome
            .Should().Be(JsonPathOutcome.Masked);
        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep).Unmasked(), AllowList).Explain("ssn").Outcome
            .Should().Be(JsonPathOutcome.Read);
        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep).MaskAny(MaskTag.Full), BlockList).Explain("ssn").Outcome
            .Should().Be(JsonPathOutcome.Masked);
    }

    public static TheoryData<string> MaskFunctionKinds => ["MaskAny", "MaskStr", "MaskRawValue"];

    [Theory]
    [MemberData(nameof(MaskFunctionKinds))]
    public void MaskFunction_UnderValueCap_ReceivesTheWholeValue(string kind)
    {
        var observer = JsonObserver.Obj(r => _ = kind switch
        {
            "MaskAny" => r.Match("card").MaskAny((s, _) => "***" + s![^4..]),
            "MaskStr" => r.Match("card").MaskStr((s, _) => "***" + s![^4..]),
            _ => r.Match("card").MaskRawValue((s, _) => "***" + s![^4..]),
        });

        observer.Mask("""{"card":"1111222233334444"}""", out var result, new JsonObserverOptions(MaxValueBytes: 8))
            .Should().Be("""{"card":"***4444"}""");
        result.Status.Should().Be(MaskStatus.Masked);
    }

    [Fact]
    public void MaskFunction_UnderValueCap_ReceivesTheWholeValue_FromSegments()
    {
        var observer = JsonObserver.Obj(r => r.Match("card").MaskAny((s, _) => "***" + s![^4..]));
        var output = new ArrayBufferWriter<byte>();

        var result = observer.Mask(SequenceInputTests.Split("""{"card":"1111222233334444"}"""u8.ToArray(), 3), output,
            new JsonObserverOptions(MaxValueBytes: 8));

        Encoding.UTF8.GetString(output.WrittenSpan).Should().Be("""{"card":"***4444"}""");
        result.Status.Should().Be(MaskStatus.Masked);
    }

    [Fact]
    public void MaskOutput_LongerThanValueCap_IsNotCut()
    {
        var observer = JsonObserver.Obj(r => r.Match("card").MaskAny((_, _) => "replaced-by-a-long-mask"));

        observer.Mask("""{"card":"1"}""", out var result, new JsonObserverOptions(MaxValueBytes: 8))
            .Should().Be("""{"card":"replaced-by-a-long-mask"}""");
        result.Status.Should().Be(MaskStatus.Masked);
    }

    [Fact]
    public void Hash_UnderValueCap_IsNotCut()
    {
        var observer = JsonObserver.Obj(r => r.Match("card").MaskAny(MaskTag.Hash));
        var options = new JsonObserverOptions(MaxValueBytes: 8, HashKey: "0123456789abcdef0123456789abcdef"u8.ToArray());

        var masked = observer.Mask("""{"card":"1111222233334444"}""", out var result, options)!;
        var uncapped = observer.Mask("""{"card":"1111222233334444"}""", options with { MaxValueBytes = int.MaxValue });

        masked.Should().Be(uncapped);
        JsonDocument.Parse(masked).RootElement.GetProperty("card").GetString().Should().StartWith("hash:").And.NotContain("…");
        result.Status.Should().Be(MaskStatus.Masked);
    }

    [Fact]
    public void UnmaskedValue_UnderValueCap_IsStillCut()
    {
        JsonObserver.Obj(JsonObserverValuePolicies.BlockList)
            .Mask("""{"note":"1111222233334444"}""", out var result, new JsonObserverOptions(MaxValueBytes: 8))
            .Should().Be("""{"note":"11112222…"}""");
        result.Status.Should().Be(MaskStatus.Truncated);
    }

    private const string CaseCorpus =
        """{"driverLicense":"A1","DRiverLicensE":"A2","driverlicense":"A3","driverLicense":"A4","drіverLicense":"A5","drİverLicense":"A6"}""";

    public static TheoryData<string, bool, string> CaseTruthTable => new()
    {
        { "Match", true, "A1 A2 A3 A4" },
        { "Match", false, "A1 A4" },
        { "Regex", true, "A1 A2 A3 A4" },
        { "Regex", false, "A1 A4" },
        { "Function", true, "A1 A2 A3 A4" },
        { "Function", false, "A1 A4" },
        { "RelativeRegex", true, "A1 A2 A3 A4" },
        { "RelativeRegex", false, "A1 A4" },
    };

    [Theory]
    [MemberData(nameof(CaseTruthTable))]
    public void CaseOption_ReachesEveryMatcher(string matcher, bool caseInsensitive, string expectedMasked)
    {
        PropMatchingStrategy match = matcher switch
        {
            "Match" => "driverLicense",
            "Function" => new PropMatchingStrategy((name, comparison) => string.Equals(name, "driverLicense", comparison)),
            _ => PropMatches.Regex(new Regex("^driverLicense$")),
        };
        var observer = matcher == "RelativeRegex"
            ? JsonObserver.Obj(JsonObserverValuePolicies.Relative(b => b.Match(match).MaskAny(MaskTag.Full), JsonObserverValuePolicies.BlockList))
            : JsonObserver.Obj(r => r.Match(match).MaskAny(MaskTag.Full), JsonObserverValuePolicies.BlockList);

        var output = observer.Mask(CaseCorpus, new JsonObserverOptions(PropertyNameCaseInsensitive: caseInsensitive))!;

        var masked = JsonDocument.Parse(output).RootElement.EnumerateObject()
            .Select((p, i) => (Key: $"A{i + 1}", Value: p.Value.GetString()))
            .Where(p => p.Value == "***")
            .Select(p => p.Key);
        string.Join(' ', masked).Should().Be(expectedMasked);
    }

    [Fact]
    public void Regex_WithExplicitIgnoreCase_StaysCaseInsensitive()
    {
        var observer = JsonObserver.Obj(
            r => r.Match(PropMatches.Regex(new Regex("^token$", RegexOptions.IgnoreCase))).MaskAny(MaskTag.Full),
            JsonObserverValuePolicies.BlockList);

        observer.Mask("""{"Token":"a"}""", new JsonObserverOptions(PropertyNameCaseInsensitive: false)).Should().Be("""{"Token":"***"}""");
    }

    private static void Keep(string? value, Holder holder) => holder.Value = value;

    public sealed class Holder
    {
        public string? Value { get; set; }

        public int? Number { get; set; }
    }
}
