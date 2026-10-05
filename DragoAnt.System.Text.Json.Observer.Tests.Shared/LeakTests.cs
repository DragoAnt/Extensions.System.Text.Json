using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using DragoAnt.Observer;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class LeakTests
{
    private const string Ssn = """{"ssn":"123-45-6789","x":"y"}""";

    private static JsonValuePolicy<Holder> AllowList => ValuePolicy.AllowList;
    private static JsonValuePolicy<Holder> BlockList => ValuePolicy.BlockList;
    private static JsonValuePolicy<Holder> NullList => ValuePolicy.NullList;

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
        var observer = JsonObserver.Obj<Holder>(JsonValuePolicy.AnyDepth<Holder>(b => b.Match("ssn").ReadStr(Keep)));

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

        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep).Mask(MaskTag.Last4), BlockList).Mask(Ssn, holder)
            .Should().Be("""{"ssn":"***6789","x":"y"}""");
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Mask_ThenRead_OnOneMatch_ReadsAndMasks()
    {
        var holder = new Holder();

        JsonObserver.Obj<Holder>(r => r.Match("ssn").Mask(MaskTag.Full).Match("ssn").ReadStr(Keep), BlockList).Mask(Ssn, holder)
            .Should().Be("""{"ssn":"***","x":"y"}""");
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Read_ThenMask_InRelativePolicy_ReadsAndMasks()
    {
        var holder = new Holder();
        var observer = JsonObserver.Obj<Holder>(JsonValuePolicy.AnyDepth<Holder>(
            b => b.Match("ssn").ReadStr(Keep).Mask(MaskTag.Full), BlockList));

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
        JsonObserver.Array<Holder>(a => a.Mask(MaskTag.Last4).ReadStr(Keep), BlockList).Mask("""["123-45-6789"]""", masked)
            .Should().Be("""["***6789"]""");
        unmasked.Value.Should().Be("123-45-6789");
        masked.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Read_ExtractionOnly_StillReads()
    {
        var holder = new Holder();

        JsonObserver.Obj<Holder>(r => r.Match("ssn").Mask(MaskTag.Full).Match("ssn").ReadStr(Keep)).Read(Ssn, holder)
            .Status.Should().Be(MaskStatus.Masked);
        holder.Value.Should().Be("123-45-6789");
    }

    [Fact]
    public void Explain_Read_ReportsWhatIsWritten()
    {
        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep), AllowList).Explain("ssn").Outcome
            .Should().Be(PathOutcome.Masked);
        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep).Unmasked(), AllowList).Explain("ssn").Outcome
            .Should().Be(PathOutcome.Read);
        JsonObserver.Obj<Holder>(r => r.Match("ssn").ReadStr(Keep).Mask(MaskTag.Full), BlockList).Explain("ssn").Outcome
            .Should().Be(PathOutcome.Masked);
    }

    public static TheoryData<string> MaskFunctionKinds => ["MaskAny", "MaskStr", "MaskRawValue"];

    [Theory]
    [MemberData(nameof(MaskFunctionKinds))]
    public void MaskFunction_UnderValueCap_ReceivesTheWholeValue(string kind)
    {
        var observer = JsonObserver.Obj(r => _ = kind switch
        {
            "MaskAny" => r.Match("card").Mask((s, _) => "***" + s![^4..]),
            "MaskStr" => r.Match("card").Mask((s, _) => "***" + s![^4..], MaskNulls.Mask),
            _ => r.Match("card").Mask((s, _) => "***" + s![^4..], MaskNulls.Mask),
        });

        observer.Mask("""{"card":"1111222233334444"}""", out var result, new JsonObserverOptions { MaxValueBytes = 8 })
            .Should().Be("""{"card":"***4444"}""");
        result.Status.Should().Be(MaskStatus.Masked);
    }

    [Fact]
    public void MaskFunction_UnderValueCap_ReceivesTheWholeValue_FromSegments()
    {
        var observer = JsonObserver.Obj(r => r.Match("card").Mask((s, _) => "***" + s![^4..]));
        var output = new ArrayBufferWriter<byte>();

        var result = observer.Mask(SequenceInputTests.Split("""{"card":"1111222233334444"}"""u8.ToArray(), 3), output,
            new JsonObserverOptions { MaxValueBytes = 8 });

        Encoding.UTF8.GetString(output.WrittenSpan).Should().Be("""{"card":"***4444"}""");
        result.Status.Should().Be(MaskStatus.Masked);
    }

    [Fact]
    public void MaskOutput_LongerThanValueCap_IsNotCut()
    {
        var observer = JsonObserver.Obj(r => r.Match("card").Mask((_, _) => "replaced-by-a-long-mask"));

        observer.Mask("""{"card":"1"}""", out var result, new JsonObserverOptions { MaxValueBytes = 8 })
            .Should().Be("""{"card":"replaced-by-a-long-mask"}""");
        result.Status.Should().Be(MaskStatus.Masked);
    }

    [Fact]
    public void Hash_UnderValueCap_IsNotCut()
    {
        var observer = JsonObserver.Obj(r => r.Match("card").Mask(MaskTag.Hash));
        var options = new JsonObserverOptions { MaxValueBytes = 8, HashKey = "0123456789abcdef0123456789abcdef"u8.ToArray() };

        var masked = observer.Mask("""{"card":"1111222233334444"}""", out var result, options)!;
        var uncapped = observer.Mask("""{"card":"1111222233334444"}""", options with { MaxValueBytes = int.MaxValue });

        masked.Should().Be(uncapped);
        JsonDocument.Parse(masked).RootElement.GetProperty("card").GetString().Should().HaveLength(24).And.EndWith("==").And.NotContain("…");
        result.Status.Should().Be(MaskStatus.Masked);
    }

    [Fact]
    public void UnmaskedValue_UnderValueCap_IsStillCut()
    {
        JsonObserver.Obj(ValuePolicy.BlockList)
            .Mask("""{"note":"1111222233334444"}""", out var result, new JsonObserverOptions { MaxValueBytes = 8 })
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
        NameMatch match = matcher switch
        {
            "Match" => "driverLicense",
            "Function" => new NameMatch((name, comparison) => string.Equals(name, "driverLicense", comparison)),
            _ => Names.Regex(new Regex("^driverLicense$")),
        };
        var observer = matcher == "RelativeRegex"
            ? JsonObserver.Obj(JsonValuePolicy.AnyDepth(b => b.Match(match).Mask(MaskTag.Full), ValuePolicy.BlockList))
            : JsonObserver.Obj(r => r.Match(match).Mask(MaskTag.Full), ValuePolicy.BlockList);

        var output = observer.Mask(CaseCorpus, new JsonObserverOptions { NameCaseInsensitive = caseInsensitive })!;

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
            r => r.Match(Names.Regex(new Regex("^token$", RegexOptions.IgnoreCase))).Mask(MaskTag.Full),
            ValuePolicy.BlockList);

        observer.Mask("""{"Token":"a"}""", new JsonObserverOptions { NameCaseInsensitive = false }).Should().Be("""{"Token":"***"}""");
    }

    private static void Keep(string? value, Holder holder) => holder.Value = value;

    public sealed class Holder
    {
        public string? Value { get; set; }

        public int? Number { get; set; }
    }
}
