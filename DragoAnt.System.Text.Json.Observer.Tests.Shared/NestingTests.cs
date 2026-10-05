using System.Buffers;
using System.Text;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class NestingTests
{
    private const string Secret = "S3cr3tV4l";

    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        AnyDepth(b => b.Match("password").Mask((_, _) => "***", MaskNulls.Mask), BlockList));

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
    public void Mask_When10000Levels_ReturnsInvalid()
    {
        var utf8 = Encoding.UTF8.GetBytes(Nested(10_000));

        var result = Observer.Mask(utf8, new ArrayBufferWriter<byte>());
        var deep = Observer.Mask(utf8, new ArrayBufferWriter<byte>(), new JsonObserverOptions { MaxDepth = 20_000 });

        result.Status.Should().Be(MaskStatus.Invalid);
        deep.Status.Should().BeOneOf(MaskStatus.Invalid, MaskStatus.Masked);
    }
}
