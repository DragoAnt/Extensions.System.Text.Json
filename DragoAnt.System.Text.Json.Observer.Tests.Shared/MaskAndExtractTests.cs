using System.Globalization;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class MaskAndExtractTests
{
    private static readonly JsonObserver<Extracted> Observer = JsonObserver.Obj<Extracted>(
        b => b
            .Match("id").ReadInt((v, c) => c.Id = v)
            .Match("active").ReadBool((v, c) => c.Active = v)
            .Match("amount").ReadDecimal((v, c) => c.Amount = v)
            .Match("big").ReadLong((v, c) => c.Big = v)
            .Match("name").ReadStr((v, c) => c.Name = v)
            .Match("raw").ReadRaw((v, c) => c.Raw = v)
            .Match("none").ReadInt((v, c) => c.None = v ?? -1),
        JsonValuePolicy.AnyDepth<Extracted>(r => r.Match("password").Mask("***"), ValuePolicy.BlockList));

    [Fact]
    public void MaskAndExtract_KeepsJsonTypes()
    {
        var context = new Extracted();

        var masked = Observer.Mask(
            """{"id":42,"active":true,"amount":1.50,"big":9007199254740993,"name":"bob","raw":1e2,"none":null,"password":"p"}""",
            context);

        masked.Should().Be("""{"id":42,"active":true,"amount":1.50,"big":9007199254740993,"name":"bob","raw":1e2,"none":null,"password":"***"}""");
        context.Should().BeEquivalentTo(new Extracted
        {
            Id = 42, Active = true, Amount = 1.50m, Big = 9007199254740993, Name = "bob", Raw = "1e2", None = -1,
        });
    }

    [Fact]
    public void ReadDecimal_InvariantCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var context = new Extracted();

            Observer.Mask("""{"amount":1.5}""", context).Should().Be("""{"amount":1.5}""");
            context.Amount.Should().Be(1.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    public sealed class Extracted
    {
        public int? Id { get; set; }
        public bool? Active { get; set; }
        public decimal? Amount { get; set; }
        public long? Big { get; set; }
        public string? Name { get; set; }
        public string? Raw { get; set; }
        public int? None { get; set; }
    }
}
