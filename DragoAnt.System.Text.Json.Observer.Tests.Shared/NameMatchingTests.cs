using System.Text;
using DragoAnt.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class NameMatchingTests
{
    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        AnyDepth(b => b
                .Match("password").Mask((_, _) => "***", MaskNulls.Mask)
                .Match(Names.OneOf("pin", "cvv")).Mask((_, _) => "***", MaskNulls.Mask)
                .Match(Names.EndsWith("Token")).Mask((_, _) => "***", MaskNulls.Mask)
                .Match(Names.StartsWith("secret")).Mask((_, _) => "***", MaskNulls.Mask)
                .Match(Names.Contains("Email")).Mask((_, _) => "***", MaskNulls.Mask)
                .Path("card", "number").Mask((_, _) => "***", MaskNulls.Mask),
            BlockList));

    [Theory]
    [InlineData("""{"PASSWORD":"x"}""", """{"PASSWORD":"***"}""")]
    [InlineData("""{"Pin":"x","CVV":"y","pan":"z"}""", """{"Pin":"***","CVV":"***","pan":"z"}""")]
    [InlineData("""{"accessTOKEN":"x","tokens":"y"}""", """{"accessTOKEN":"***","tokens":"y"}""")]
    [InlineData("""{"SecretKey":"x","noSecret":"y"}""", """{"SecretKey":"***","noSecret":"y"}""")]
    [InlineData("""{"userEMAILaddress":"x"}""", """{"userEMAILaddress":"***"}""")]
    [InlineData("""{"Card":{"Number":"x","name":"y"}}""", """{"Card":{"Number":"***","name":"y"}}""")]
    public void CaseInsensitive_Utf8Match(string json, string expected) => Observer.Mask(json).Should().Be(expected);

    [Fact]
    public void EscapedName_StillMatches() =>
        Observer.Mask("""{"pass\u0077ord":"x","c\u0061rd":{"n\u0075mber":"y"}}""")
            .Should().Be("""{"password":"***","card":{"number":"***"}}""");

    [Fact]
    public void NonAsciiName_StillMatches() =>
        JsonDocument.Parse(Observer.Mask("""{"пароль":"x","password":"y"}""")!).RootElement.EnumerateObject().Select(p => p.Name + "=" + p.Value.GetString()).Should().Equal("пароль=x", "password=***");

    [Fact]
    public void CustomStrategy_GetsDecodedName()
    {
        var names = new List<string?>();
        var observer = JsonObserver.Obj(_ => { }, AnyDepth(b => b.Match(new NameMatch(n =>
        {
            names.Add(n);
            return false;
        })).Mask((_, _) => "***", MaskNulls.Mask), BlockList));

        observer.Mask("""{"a\u0062":1,"c":[2]}""");

        names.Should().Equal("ab", "c", null);
    }

    [Fact]
    public void OneOf_NoClosureAllocation()
    {
        var observer = JsonObserver.Obj<Counter>(
            _ => { },
            JsonValuePolicy.AnyDepth<Counter>(b => b
                .Match(Names.OneOf("pin", "cvv", "password")).ReadStr((_, c) => c.Hits++)
                .Match("accessToken").ReadStr((_, c) => c.Hits++)
                .Match(Names.EndsWith("Token")).ReadStr((_, c) => c.Hits++)
                .Match(Names.StartsWith("secret")).ReadStr((_, c) => c.Hits++)
                .Match(Names.Contains("email")).ReadStr((_, c) => c.Hits++)));
        var json = new StringBuilder("{");
        for (var i = 0; i < 200; i++)
        {
            json.Append(i == 0 ? "" : ",").Append("\"field").Append(i).Append("\":").Append(i);
        }

        var payload = Encoding.UTF8.GetBytes(json.Append('}').ToString());
        var counter = new Counter();
        for (var i = 0; i < 5; i++)
        {
            observer.Read(payload, counter);
        }

        const int calls = 20;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < calls; i++)
        {
            observer.Read(payload, counter);
        }

        var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / calls;

        counter.Hits.Should().Be(0);
        perCall.Should().BeLessThan(200, "a property name must not allocate");
    }

    public sealed class Counter
    {
        public int Hits { get; set; }
    }
}
