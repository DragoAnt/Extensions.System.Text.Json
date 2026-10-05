using System.Buffers;
using System.Text;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class AllocationTests
{
    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        AnyDepth(b => b.Match("password").Mask("***").Path("card", "number").Mask("***", MaskNulls.Mask), BlockList));

    private static readonly JsonObserver<NoContext> Reader = JsonObserver.Any<NoContext>(
        _ => { },
        _ => { },
        JsonValuePolicy.AnyDepth<NoContext>(
            b => b.Match("password").Mask("***"),
            ValuePolicy.BlockList));

    private static readonly JsonObserverOptions IgnoreNulls = new() { IgnoreNulls = true };

    public static TheoryData<string, int, string> Budgets()
    {
        var data = new TheoryData<string, int, string>();
        foreach (var api in new[] { "span", "sequence", "ignore-nulls", "read" })
        {
            data.Add("flat", 1024, api);
            data.Add("flat", 64 * 1024, api);
            data.Add("nested", 8 * 1024, api);
            data.Add("array", 8 * 1024, api);
        }

        return data;
    }

    /// <summary>
    /// The bytes API allocates nothing per call once warm: writers are reused per thread and buffers come from the pool.
    /// </summary>
    [Theory]
    [MemberData(nameof(Budgets))]
    public void BytesApi_ReusedOutput_AllocatesNothing(string shape, int size, string api)
    {
        var utf8 = Encoding.UTF8.GetBytes(Payload(shape, size));
        var sequence = SequenceInputTests.Split(utf8, 4096);
        var output = new ArrayBufferWriter<byte>(utf8.Length * 2);

        void Call()
        {
            output.ResetWrittenCount();
            _ = api switch
            {
                "span" => Observer.Mask(utf8, output),
                "sequence" => Observer.Mask(sequence, output),
                "ignore-nulls" => Observer.Mask(utf8, output, IgnoreNulls),
                _ => Reader.Read(utf8, NoContext.Instance),
            };
        }

        for (var i = 0; i < 20; i++)
        {
            Call();
        }

        // A one-off runtime allocation (tier-up under a loaded test host) lands in one round; a real per-call cost lands in all.
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

    [Fact]
    public void NestedCallOnSameThread_GetsItsOwnWriter()
    {
        var inner = JsonObserver.Obj(AnyDepth(b => b.Match("pin").Mask("#"), BlockList));
        var outer = JsonObserver.Obj(b => b.Match("payload").Mask((v, _) => inner.Mask(v), MaskNulls.Mask), BlockList);

        outer.Mask("""{"payload":"{\"pin\":1,\"x\":2}","y":3}""")
            .Should().Be("""{"payload":"{\"pin\":\"#\",\"x\":2}","y":3}""");
    }

    [Fact]
    public void WriterSettingsChange_BetweenCalls_Respected()
    {
        const string json = """{"a":"é<","password":"x"}""";

        Observer.Mask(json).Should().Be("""{"a":"é<","password":"***"}""");
        Observer.Mask(json, new JsonObserverOptions { RelaxedEscaping = false }).Should().NotContain("é").And.Contain((char)92 + "u003C").And.EndWith(",\"password\":\"***\"}");
        Observer.Mask(json, new JsonObserverOptions { Indented = true }).Should().Contain(Environment.NewLine);
        Observer.Mask(json).Should().Be("""{"a":"é<","password":"***"}""");
    }

    internal static string Payload(string shape, int size)
    {
        var json = new StringBuilder();
        var i = 0;
        switch (shape)
        {
            case "flat":
                json.Append("""{"password":"secret","card":{"number":"4111111111111111"}""");
                while (json.Length < size)
                {
                    json.Append(",\"field").Append(i).Append("\":\"value ").Append(i++).Append('"');
                }

                return json.Append('}').ToString();
            case "nested":
                json.Append("{\"password\":\"secret\"");
                while (json.Length < size)
                {
                    json.Append(",\"group").Append(i).Append("\":{\"id\":").Append(i).Append(",\"inner\":{\"name\":\"n").Append(i++)
                        .Append("\",\"card\":{\"number\":\"4111111111111111\"}}}");
                }

                return json.Append('}').ToString();
            default:
                json.Append('[');
                while (json.Length < size)
                {
                    json.Append(i == 0 ? "" : ",").Append("{\"id\":").Append(i).Append(",\"sku\":\"A").Append(i++)
                        .Append("\",\"password\":\"p\",\"qty\":2,\"price\":1.50}");
                }

                return json.Append(']').ToString();
        }
    }
}
