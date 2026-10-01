using System.Text;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class NameMatchingTests
{
    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        Relative(b => b
                .Match("password").MaskStr((_, _) => "***")
                .Match(PropMatches.OneOf("pin", "cvv")).MaskStr((_, _) => "***")
                .Match(PropMatches.EndsWith("Token")).MaskStr((_, _) => "***")
                .Match(PropMatches.StartsWith("secret")).MaskStr((_, _) => "***")
                .Match(PropMatches.Contains("Email")).MaskStr((_, _) => "***")
                .Match("card", "number").MaskStr((_, _) => "***"),
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
        var observer = JsonObserver.Obj(_ => { }, Relative(b => b.Match(new PropMatchingStrategy(n =>
        {
            names.Add(n);
            return false;
        })).MaskStr((_, _) => "***"), BlockList));

        observer.Mask("""{"a\u0062":1,"c":[2]}""");

        names.Should().Equal("ab", "c", null);
    }

    [Fact]
    public void OneOf_NoClosureAllocation()
    {
        var observer = JsonObserver.Obj<Counter>(
            _ => { },
            JsonObserverValuePolicies<Counter>.Relative(b => b
                .Match(PropMatches.OneOf("pin", "cvv", "password")).ReadStr((_, c) => c.Hits++)
                .Match("accessToken").ReadStr((_, c) => c.Hits++)
                .Match(PropMatches.EndsWith("Token")).ReadStr((_, c) => c.Hits++)
                .Match(PropMatches.StartsWith("secret")).ReadStr((_, c) => c.Hits++)
                .Match(PropMatches.Contains("email")).ReadStr((_, c) => c.Hits++)));
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
