using System.Buffers;
using System.Text;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class NestingTests
{
    private const string Secret = "S3cr3tV4l";

    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        Relative(b => b.Match("password").MaskStr((_, _) => "***"), BlockList));

    internal static string Nested(int levels)
    {
        var sb = new StringBuilder();
        for (var i = 1; i < levels; i++)
        {
            sb.Append("{\"level\":");
        }

        sb.Append("{\"password\":\"").Append(Secret).Append("\"}");
        sb.Append('}', levels - 1);
        return sb.ToString();
    }

    [Fact]
    public void Mask_WhenNested17Levels_Masks()
    {
        var masked = Observer.Mask(Nested(17));

        masked.Should().NotContain(Secret).And.Contain("\"password\":\"***\"");
    }

    [Fact]
    public void Mask_WhenNested64Levels_Masks()
    {
        var masked = Observer.Mask(Nested(64));

        masked.Should().NotContain(Secret).And.Contain("\"password\":\"***\"");
    }

    [Fact]
    public void PropertyPath_ReturnsRentedArray()
    {
        var pool = ArrayPool<PropertyPath.Segment>.Shared;
        var probe = pool.Rent(16);
        pool.Return(probe);

        Observer.Mask("{\"password\":\"x\"}");

        var again = pool.Rent(16);
        pool.Return(again);
        again.Should().BeSameAs(probe);
    }
}
