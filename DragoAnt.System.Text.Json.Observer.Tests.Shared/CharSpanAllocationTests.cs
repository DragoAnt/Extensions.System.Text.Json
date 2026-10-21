using System.Buffers;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class CharSpanAllocationTests
{
    private static readonly JsonObserver Constant = JsonObserver.Any(
        _ => { },
        _ => { },
        AnyDepth(b => b.Match("password").Mask("***").Path("card", "number").Mask("***", MaskNulls.Mask), BlockList));

    private static readonly JsonObserver Tags = JsonObserver.Any(
        _ => { },
        _ => { },
        AnyDepth(b => b.Match("password").Mask(MaskTag.Hash).Path("card", "number").Mask(MaskTag.Last4), BlockList));

    private static readonly JsonObserverOptions Keyed = new JsonObserverOptions { HashKeyId = 3 }
        .WithBase64HashKey("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+Pw==");

    public static TheoryData<string, int, string> Budgets()
    {
        var data = new TheoryData<string, int, string>();
        foreach (var api in new[] { "chars-to-bytes", "chars-to-chars", "read-chars", "chars-tag-hash" })
        {
            data.Add("flat", 1024, api);
            data.Add("flat", 64 * 1024, api);
            data.Add("nested", 8 * 1024, api);
            data.Add("array", 8 * 1024, api);
        }

        return data;
    }

    /// <summary>
    /// The character API transcodes into pooled buffers, so it allocates nothing per call once warm, like the bytes API.
    /// </summary>
    [Theory]
    [MemberData(nameof(Budgets))]
    public void CharsApi_ReusedOutput_AllocatesNothing(string shape, int size, string api)
    {
        var json = AllocationTests.Payload(shape, size);
        var bytes = new ArrayBufferWriter<byte>(json.Length * 3);
        var chars = new ArrayBufferWriter<char>(json.Length * 2);

        void Call()
        {
            bytes.ResetWrittenCount();
            chars.ResetWrittenCount();
            _ = api switch
            {
                "chars-to-bytes" => Constant.Mask(json.AsSpan(), bytes),
                "chars-to-chars" => Constant.Mask(json.AsSpan(), chars),
                "chars-tag-hash" => Tags.Mask(json.AsSpan(), chars, Keyed),
                _ => Constant.Read(json.AsSpan()),
            };
        }

        for (var i = 0; i < 20; i++)
        {
            Call();
        }

        const int calls = 50;
        var perCall = long.MaxValue;
        for (var round = 0; round < 5 && perCall > 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < calls; i++)
            {
                Call();
            }

            perCall = Math.Min(perCall, (GC.GetAllocatedBytesForCurrentThread() - before) / calls);
        }

        perCall.Should().Be(0, $"{api} {shape} {size} B allocates {perCall} B per call");
    }
}
