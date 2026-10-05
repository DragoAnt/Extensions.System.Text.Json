using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class PassThroughTests
{
    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        AnyDepth(b => b.Match("password").Mask((_, _) => "***", MaskNulls.Mask), BlockList));

    [Theory]
    [InlineData("""{"a":1e2}""")]
    [InlineData("""{"a":1.50}""")]
    [InlineData("""[1E+2,0.1e-3]""")]
    public void Number_1e2_KeptVerbatim(string json) => Observer.Mask(json).Should().Be(json);

    [Fact]
    public void NegativeZero_Kept() => Observer.Mask("""{"a":-0}""").Should().Be("""{"a":-0}""");

    [Fact]
    public void Number_1e400_PassesThrough() => Observer.Mask("""{"a":1e400}""").Should().Be("""{"a":1e400}""");

    [Fact]
    public void TwentyDigitInt_Kept() =>
        Observer.Mask("""{"a":12345678901234567890}""").Should().Be("""{"a":12345678901234567890}""");

    [Fact]
    public void EscapedName_RoundTrips() =>
        Observer.Mask("""{"pass\u0077ord":"x","n\u0061me":"v\u0061l"}""").Should().Be("""{"password":"***","name":"val"}""");

    [Fact]
    public void EscapedValue_ReEscaped() =>
        JsonDocument.Parse(Observer.Mask("""{"a":"q\"t\u00e9"}""")!).RootElement.GetProperty("a").GetString().Should().Be("q\"té");
}
