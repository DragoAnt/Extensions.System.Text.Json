using System.Buffers;
using System.Text;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class MaskFlagsTests
{
    private static readonly JsonObserver Observer = JsonObserver.Any(_ => { }, _ => { }, AnyDepth(b => b.Match("pin").Mask(MaskTag.Full), BlockList));

    private static (string Output, MaskResult Result) Mask(byte[] utf8, JsonObserverOptions? options = null)
    {
        var output = new ArrayBufferWriter<byte>();
        var result = Observer.Mask(utf8, output, options);
        return (Encoding.UTF8.GetString(output.WrittenSpan), result);
    }

    private static (string Output, MaskResult Result) Mask(string json, JsonObserverOptions? options = null) => Mask(Encoding.UTF8.GetBytes(json), options);

    [Fact]
    public void CleanPayload_HasNoFlags()
    {
        var (output, result) = Mask("""{"name":"Bob","pin":1} """);

        output.Should().Be("""{"name":"Bob","pin":"***"}""");
        result.Should().Be(new MaskResult { Status = MaskStatus.Masked, BytesWritten = output.Length, FailedAtByte = -1 });
    }

    [Theory]
    [InlineData("""{"name":"Bob"}{"pin":1}""")]
    [InlineData("""{"name":"Bob"} [""")]
    [InlineData("""{"name":"Bob"} x""")]
    [InlineData("""{"name":"Bob"}]""")]
    public void DataAfterTheRoot_IsTrailingData(string json)
    {
        var (output, result) = Mask(json);

        output.Should().Be("""{"name":"Bob"}""");
        result.Status.Should().Be(MaskStatus.Truncated);
        result.Flags.Should().Be(MaskFlags.TrailingData);
        result.FailedAtByte.Should().Be(-1);
    }

    [Fact]
    public void DataAfterTheRoot_IsReportedByRead()
        => JsonObserver.Obj<NoContext>(_ => { }).Read("""{"a":1}{"b":2}""", NoContext.Instance).Flags.Should().Be(MaskFlags.TrailingData);

    [Fact]
    public void CutInput_IsInputTruncated()
    {
        var (output, result) = Mask("""{"name":"Bob","pin":98""");

        output.Should().Be("""{"name":"Bob"}""");
        result.Status.Should().Be(MaskStatus.Truncated);
        result.Flags.Should().Be(MaskFlags.InputTruncated);
        result.FailedAtByte.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OutputLimit_IsOutputCapped()
    {
        var (output, result) = Mask("""{"name":"Bob","city":"Berlin","pin":1}""", new JsonObserverOptions { MaxOutputBytes = 16 });

        output.Should().Be("""{"name":"Bob"}""");
        result.Status.Should().Be(MaskStatus.Truncated);
        result.Flags.Should().HaveFlag(MaskFlags.OutputCapped);
    }

    [Fact]
    public void LongValue_IsValueCut()
    {
        var (output, result) = Mask("""{"name":"Bartholomew"}""", new JsonObserverOptions { MaxValueBytes = 4 });

        output.Should().Be("""{"name":"Bart…"}""");
        result.Status.Should().Be(MaskStatus.Truncated);
        result.Flags.Should().Be(MaskFlags.ValueCut);
        result.FailedAtByte.Should().Be(-1);
    }

    public static TheoryData<byte[]> InvalidUtf8 => new()
    {
        Bytes("{\"name\":\"a", [0xC3], "b\"}"),
        Bytes("{\"name\":\"a", [0xC0, 0xAF], "\"}"),
        Bytes("{\"name\":\"", [0xED, 0xA0, 0x80], "\"}"),
        Bytes("{\"n", [0xC3], "\":1}"),
    };

    private static byte[] Bytes(string before, byte[] invalid, string after) =>
        [.. Encoding.UTF8.GetBytes(before), .. invalid, .. Encoding.UTF8.GetBytes(after)];

    [Theory]
    [MemberData(nameof(InvalidUtf8))]
    public void InvalidUtf8_InAValueOrName_IsReported(byte[] utf8)
    {
        var (output, result) = Mask(utf8);

        output.Should().Contain(@"\uFFFD");
        result.Status.Should().Be(MaskStatus.Truncated);
        result.Flags.Should().Be(MaskFlags.InvalidUtf8Replaced);
    }

    [Fact]
    public void InvalidUtf8_InAMaskedValue_IsNotReported()
    {
        var (output, result) = Mask(Bytes("{\"pin\":\"a", [0xC3], "\"}"));

        output.Should().Be("""{"pin":"***"}""");
        result.Should().Be(new MaskResult { Status = MaskStatus.Masked, BytesWritten = output.Length, FailedAtByte = -1 });
    }

    [Fact]
    public void TooDeep_IsDepth()
    {
        var json = string.Concat(Enumerable.Repeat("""{"a":""", 10)) + "1" + new string('}', 10);

        var (output, result) = Mask(json, new JsonObserverOptions { MaxDepth = 5 });

        result.Status.Should().Be(MaskStatus.Invalid);
        result.Flags.Should().Be(MaskFlags.Depth);
        JsonDocument.Parse(output).RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void InvalidJson_HasNoDepthFlag()
        => Mask("""{"a":tru e}""").Result.Should().Match<MaskResult>(r => r.Status == MaskStatus.Invalid && r.Flags == MaskFlags.None);

    [Fact]
    public void SeveralCauses_Combine()
    {
        var (_, result) = Mask("""{"name":"Bartholomew","x":"y"}{}""", new JsonObserverOptions { MaxValueBytes = 4 });

        result.Flags.Should().Be(MaskFlags.ValueCut | MaskFlags.TrailingData);
        result.Status.Should().Be(MaskStatus.Truncated);
    }
}
