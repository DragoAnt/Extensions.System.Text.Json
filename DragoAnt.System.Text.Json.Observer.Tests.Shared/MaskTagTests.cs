using System.Text;
using DragoAnt.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class MaskTagTests
{
    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        AnyDepth(b => b
                .Match("full").Mask(MaskTag.Full)
                .Match("last4").Mask(MaskTag.Last4)
                .Match("hash").Mask(MaskTag.Hash)
                .Match("omit").Mask(MaskKind.Null),
            BlockList));

    private static JsonElement Mask(string json, JsonObserverOptions? options = null)
    {
        var (result, output) = BytesApiTests.Mask(Observer, json, options);
        result.Status.Should().Be(MaskStatus.Masked);
        return JsonDocument.Parse(output).RootElement.Clone();
    }

    [Fact]
    public void Full_MasksEveryJsonType() =>
        Observer.Mask("""[{"full":"x"},{"full":5},{"full":true},{"full":{"z":1}},{"full":[1]},{"full":null}]""")
            .Should().Be("""[{"full":"***"},{"full":"***"},{"full":"***"},{"full":"***"},{"full":"***"},{"full":null}]""");

    [Fact]
    public void Last4_ShortValue_Full()
    {
        var root = Mask("""[{"last4":"4111111111111111"},{"last4":4111111111111111},{"last4":"1234567"},{"last4":"ИванИванИван"},{"last4":true}]""");

        root.EnumerateArray().Select(e => e.GetProperty("last4").GetString())
            .Should().Equal("***1111", "***1111", "***", "***Иван", "***");
    }

    [Fact]
    public void Hash_StableAcrossCalls()
    {
        var first = Mask("""{"hash":"S3cr3t"}""").GetProperty("hash").GetString();
        var second = Mask("""{"hash":"S3cr3t"}""").GetProperty("hash").GetString();
        var other = Mask("""{"hash":"S3cr3u"}""").GetProperty("hash").GetString();
        var escaped = Mask("{\"hash\":\"S" + (char)92 + "u0033cr3t\"}").GetProperty("hash").GetString();

        first.Should().HaveLength(24).And.EndWith("==").And.Be(second).And.Be(escaped).And.NotBe(other);
    }

    [Fact]
    public void Hash_KeyedDiffers()
    {
        var keyA = new JsonObserverOptions { HashKey = Encoding.UTF8.GetBytes("key-a") };
        var keyB = new JsonObserverOptions { HashKey = Encoding.UTF8.GetBytes("key-b") };

        var a = Mask("""{"hash":"S3cr3t"}""", keyA).GetProperty("hash").GetString();
        var again = Mask("""{"hash":"S3cr3t"}""", keyA with { }).GetProperty("hash").GetString();
        var b = Mask("""{"hash":"S3cr3t"}""", keyB).GetProperty("hash").GetString();

        a.Should().Be(again).And.NotBe(b);
    }

    [Fact]
    public void Omit_WritesNull() =>
        Observer.Mask("""{"omit":"x","o":{"omit":[1,2]}}""").Should().Be("""{"omit":null,"o":{"omit":null}}""");

    [Fact]
    public void Tag_PassedToCustomStrategy()
    {
        var strategy = new RecordingStrategy();

        var (_, output) = BytesApiTests.Mask(
            Observer,
            """{"full":"a","hash":7,"last4":{"x":1},"omit":true}""",
            new JsonObserverOptions { Strategy = strategy });

        output.Should().Be("""{"full":"?","hash":"?","last4":"?","omit":"?"}""");
        strategy.Calls.Should().Equal(
            "Full String a",
            "Hash Number 7",
            "Last4 Object ",
            "Null Boolean true");
    }

    private sealed record Classification(string Taxonomy, string Name);

    private static readonly Classification Pii = new("Demo", "Pii");

    private static readonly JsonObserver KeyedObserver = JsonObserver.Obj(AnyDepth(b => b
            .Match("email").Mask(MaskTag.Custom(Pii))
            .Match("token").Mask(MaskTag.Create(MaskKind.Hash, "secret"))
            .Match("plain").Mask(MaskTag.Last4),
        BlockList));

    [Fact]
    public void CustomKey_ReachesStrategyWithoutCasts()
    {
        var strategy = new KeyedStrategy();

        var (_, output) = BytesApiTests.Mask(
            KeyedObserver,
            """{"email":"a@b.c","token":"t0k3n","plain":"12345678"}""",
            new JsonObserverOptions { Strategy = strategy });

        output.Should().Be("""{"email":"Pii","token":"secret","plain":"none"}""");
    }

    [Fact]
    public void CustomKey_DefaultStrategy_FallsBackToKind()
    {
        var root = Mask("""{"full":"x"}""");
        root.GetProperty("full").GetString().Should().Be("***");

        var masked = KeyedObserver.Mask("""{"email":"a@b.c","token":"t0k3n","plain":"12345678"}""");

        masked.Should().MatchRegex("""^\{"email":"\*\*\*","token":"[A-Za-z0-9+/]{22}==","plain":"\*\*\*5678"\}$""");
    }

    [Fact]
    public void MaskTag_KeyEqualityAndAccessors()
    {
        var custom = MaskTag.Custom(Pii);

        custom.Kind.Should().Be(MaskKind.Custom);
        custom.Should().Be(MaskTag.Custom(new Classification("Demo", "Pii")));
        custom.Should().NotBe(MaskTag.Custom(new Classification("Demo", "Secret")));
        custom.TryGetKey<Classification>(out var key).Should().BeTrue();
        key.Should().Be(Pii);
        custom.TryGetKey<string>(out _).Should().BeFalse();
        ((MaskTag)MaskKind.Last4).Should().Be(MaskTag.Last4).And.Be(MaskTag.Create(MaskKind.Last4, null));
        MaskTag.Last4.Key.Should().BeNull();
        var build = () => MaskTag.Custom(null!);
        build.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ContextStrategy_ReceivesPropertyNameAndPath()
    {
        var strategy = new DiscriminatingStrategy();
        var rules = JsonObserver.Obj(
            root => root
                .Match("user").Obj(u => u.Match("email").Mask(MaskTag.Custom(Pii)))
                .Match("tags").Array(t => t.Mask(MaskTag.Hash)),
            AnyDepth(b => b.Match("phone").Mask(MaskTag.Last4), BlockList));
        var shape = JsonObserver.FromShape(JsonShape.Object(
            ("cards", JsonShape.Array(JsonShape.Object(("number", JsonShape.Masked(MaskTag.Last4)))))));
        var options = new JsonObserverOptions { Strategy = strategy };

        rules.Mask("""{"user":{"email":"a@b.c"},"tags":["x"],"o":{"phone":"12"}}""", options)
            .Should().Be("""{"user":{"email":"a@b.c:email"},"tags":["x:"],"o":{"phone":"12:phone"}}""");
        shape.Mask("""{"cards":[{"number":"4111"},{"number":"4222","cvv":1}]}""", options)
            .Should().Be("""{"cards":[{"number":"4111:number"},{"number":"4222:number","cvv":"***"}]}""");
        strategy.Calls.Should().Equal(
            "Custom user.email email",
            "Hash tags[0] []",
            "Last4 o.phone phone",
            "Last4 cards[0].number number",
            "Last4 cards[1].number number",
            "Full cards[1].cvv cvv");
    }

    [Fact]
    public void OldSignatureOnly_StillCalled()
    {
        var strategy = new RecordingStrategy();

        Observer.Mask("""{"full":"a"}""", new JsonObserverOptions { Strategy = strategy }).Should().Be("""{"full":"?"}""");
        strategy.Calls.Should().Equal("Full String a");
    }

    [Fact]
    public void Delegating_BehavesLikeDefault() =>
        Observer.Mask("""{"last4":"4111111111111111","omit":1}""", new JsonObserverOptions { Strategy = new NoOverrideStrategy() })
            .Should().Be("""{"last4":"***1111","omit":null}""");

    private sealed class NoOverrideStrategy : ValueMaskStrategy
    {
        public override void Mask(in MaskContext context, MaskValueWriter output) => Default.Mask(context, output);
    }

    private sealed class DiscriminatingStrategy : ValueMaskStrategy
    {
        public List<string> Calls { get; } = [];

        public override void Mask(in MaskContext context, MaskValueWriter output)
        {
            var name = Encoding.UTF8.GetString(context.Name);
            Calls.Add($"{context.Tag.Kind} {context.Path.ToString()} {(context.IsArrayItem ? "[]" : name)}");
            if (context.Kind is ValueKind.String)
            {
                output.String($"{Encoding.UTF8.GetString(context.Value)}:{name}");
            }
            else
            {
                Default.Mask(context, output);
            }
        }
    }

    private sealed class KeyedStrategy : ValueMaskStrategy
    {
        public override void Mask(in MaskContext context, MaskValueWriter output)
        {
            switch (context.Tag.Key)
            {
                case Classification classification:
                    output.String(classification.Name);
                    break;
                case string name:
                    output.String(name);
                    break;
                default:
                    output.String("none");
                    break;
            }
        }
    }

    private sealed class RecordingStrategy : ValueMaskStrategy
    {
        public List<string> Calls { get; } = [];

        public override void Mask(in MaskContext context, MaskValueWriter output)
        {
            Calls.Add($"{context.Tag.Kind} {context.Kind} {Encoding.UTF8.GetString(context.Value)}");
            output.String("?");
        }
    }
}
