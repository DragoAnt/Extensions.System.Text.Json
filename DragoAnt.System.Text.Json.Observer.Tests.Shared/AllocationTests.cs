using System.Buffers;
using System.Text;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class AllocationTests
{
    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        Relative(b => b.Match("password").MaskAny("***").Match("card", "number").MaskStr("***"), BlockList));

    public static TheoryData<string, int, long> Budgets => new()
    {
        { "flat", 1024, 512 },
        { "flat", 64 * 1024, 512 },
        { "nested", 8 * 1024, 512 },
        { "array", 8 * 1024, 512 },
    };

    [Theory]
    [MemberData(nameof(Budgets))]
    public void BytesApi_ReusedOutput_StaysWithinBudget(string shape, int size, long budget)
    {
        var utf8 = Encoding.UTF8.GetBytes(Payload(shape, size));
        var output = new ArrayBufferWriter<byte>(utf8.Length * 2);
        for (var i = 0; i < 20; i++)
        {
            output.ResetWrittenCount();
            Observer.Mask(utf8, output);
        }

        const int calls = 50;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < calls; i++)
        {
            output.ResetWrittenCount();
            Observer.Mask(utf8, output);
        }

        var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / calls;

        perCall.Should().BeLessThanOrEqualTo(budget, $"{shape} {size} B allocates {perCall} B per call");
    }

    private static string Payload(string shape, int size)
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
