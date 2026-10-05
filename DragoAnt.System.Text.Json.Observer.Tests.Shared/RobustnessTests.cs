using System.Buffers;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using DragoAnt.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class RobustnessTests
{
    private const string Secret = "S3cr3tV4l";

    private static readonly JsonObserver Observer = JsonObserver.Any(
        _ => { },
        _ => { },
        AnyDepth(b => b.Match("password").Mask("***").Match("pin").Mask("***", MaskNulls.Mask), BlockList));

    private static readonly JsonObserver AllowListObserver = JsonObserver.Any(
        o => o.Match("user").Unmasked().Match("ok").Unmasked(),
        _ => { });

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes(
        $$"""{"user":"bob","password":"{{Secret}}","items":[{"pin":"{{Secret}}","n":1.5},{"pin":["{{Secret}}"]}],"ok":true}""");

    [Fact]
    public void Fuzz_CorruptedBytes_OutputParsesAndNeverLeaks()
    {
        var random = new Random(20261003);
        for (var i = 0; i < 5_000; i++)
        {
            var corrupted = Payload.ToArray();
            for (var flips = random.Next(1, 4); flips > 0; flips--)
            {
                corrupted[random.Next(corrupted.Length)] = (byte)random.Next(256);
            }

            var (result, output) = BytesApiTests.Mask(AllowListObserver, corrupted.AsSpan(0, random.Next(corrupted.Length + 1)));

            output.Should().NotContain(Secret);
            if (result.BytesWritten > 0)
            {
                var parse = () => JsonDocument.Parse(output).Dispose();
                parse.Should().NotThrow(output);
            }
        }
    }

    [Fact]
    public void Fuzz_FromShape_EveryPrefix_NeverThrowsNeverLeaks()
    {
        var shape = JsonShape.FromTypeInfo(
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }.GetTypeInfo(typeof(JsonShapeTests.Customer)),
            p => p.Name is "password" or "secretCode" ? MaskTag.Full : null);
        var observer = JsonObserver.FromShape(shape);
        var json = Encoding.UTF8.GetBytes(
            $$$"""{"id":1,"name":"bob","password":"{{{Secret}}}","orders":[{"sku":"A","secretCode":"{{{Secret}}}"}],"extra":{"x":"{{{Secret}}}"}}""");

        for (var cut = 0; cut <= json.Length; cut++)
        {
            var (result, output) = BytesApiTests.Mask(observer, json.AsSpan(0, cut));

            output.Should().NotContain(Secret);
            if (result.BytesWritten > 0)
            {
                var parse = () => JsonDocument.Parse(output).Dispose();
                parse.Should().NotThrow(output);
            }
        }
    }

    public static TheoryData<byte[]> InvalidUtf8 =>
    [
        new byte[] { (byte)'{', (byte)'"', (byte)'a', (byte)'"', (byte)':', (byte)'"', 0xC3, (byte)'"', (byte)'}' },
        new byte[] { (byte)'{', (byte)'"', (byte)'a', (byte)'"', (byte)':', (byte)'"', 0xC0, 0xAF, (byte)'"', (byte)'}' },
        new byte[] { (byte)'{', (byte)'"', 0xFF, (byte)'"', (byte)':', (byte)'1', (byte)'}' },
    ];

    [Theory]
    [MemberData(nameof(InvalidUtf8))]
    public void InvalidUtf8_NeverThrows_OutputIsValidJson(byte[] utf8)
    {
        var (result, output) = BytesApiTests.Mask(Observer, utf8);

        result.Status.Should().BeOneOf(MaskStatus.Masked, MaskStatus.Invalid);
        if (output.Length > 0)
        {
            var parse = () => JsonDocument.Parse(output).Dispose();
            parse.Should().NotThrow();
        }
    }

    [Theory]
    [InlineData(20, MaskStatus.Masked)]
    [InlineData(19, MaskStatus.Invalid)]
    public void MaxDepthOption_Respected(int maxDepth, MaskStatus expected)
    {
        var json = new string('[', 20) + new string(']', 20);

        BytesApiTests.Mask(Observer, json, new JsonObserverOptions { MaxDepth = maxDepth }).Result.Status.Should().Be(expected);
    }

    [Fact]
    public void Bom_Skipped()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("""{"password":"x"}""")).ToArray();

        BytesApiTests.Mask(Observer, withBom).Output.Should().Be("""{"password":"***"}""");
    }

    [Fact]
    public void MaxOutputBytes_ExactOutputLength_Masked()
    {
        const string json = """{"password":"x","n":1}""";
        var full = BytesApiTests.Mask(Observer, json).Output;

        var (result, output) = BytesApiTests.Mask(Observer, json, new JsonObserverOptions { MaxOutputBytes = Encoding.UTF8.GetByteCount(full) });

        result.Status.Should().Be(MaskStatus.Masked);
        output.Should().Be(full);
    }

    [Fact]
    public void Concurrency_SharedObserver_BytesApi_SameOutput()
    {
        var expected = BytesApiTests.Mask(Observer, Payload).Output;
        var mismatches = 0;

        Parallel.For(0, 8, _ =>
        {
            var output = new ArrayBufferWriter<byte>();
            for (var i = 0; i < 2_000; i++)
            {
                output.ResetWrittenCount();
                Observer.Mask(Payload, output);
                if (Encoding.UTF8.GetString(output.WrittenSpan) != expected)
                {
                    Interlocked.Increment(ref mismatches);
                }
            }
        });

        mismatches.Should().Be(0);
    }

    [Fact]
    public void Concurrency_SharedShapeObserver_SameOutput()
    {
        var observer = JsonObserver.FromShape(JsonShape.Object(("id", JsonShape.Scalar), ("password", JsonShape.Masked(MaskTag.Hash))));
        var options = new JsonObserverOptions { HashKey = "key"u8.ToArray() };
        var expected = observer.Mask("""{"id":1,"password":"p","x":2}""", options);
        var results = new string?[8 * 500];

        Parallel.For(0, results.Length, i => results[i] = observer.Mask("""{"id":1,"password":"p","x":2}""", options));

        results.Should().AllBe(expected);
    }

    [Fact]
    public void StringApi_ReturnsInputBufferCleared()
    {
        var secret = new string('q', 3000) + Secret;
        var json = $$"""{"password":"{{secret}}"}""";
        Observer.Mask(json);

        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(json));
        try
        {
            Encoding.UTF8.GetString(buffer).Should().NotContain(Secret);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
