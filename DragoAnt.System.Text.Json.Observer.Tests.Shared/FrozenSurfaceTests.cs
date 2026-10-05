using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class FrozenSurfaceTests
{
    private const string Json = """{"user":"ännä","password":"s3cret","card":{"number":"4111111111111111"}}""";

    private static readonly JsonObserver Observer = JsonObserver.Obj(AnyDepth(b => b
            .Match("password").Mask(MaskTag.Full)
            .Path("card", "number").Mask(MaskTag.Last4),
        BlockList));

    [Fact]
    public void CharSpan_ToBytes_EqualsTheStringApi()
    {
        var output = new ArrayBufferWriter<byte>();

        var result = Observer.Mask(Json.AsSpan(), output);

        Encoding.UTF8.GetString(output.WrittenSpan).Should().Be(Observer.Mask(Json));
        result.Status.Should().Be(MaskStatus.Masked);
        result.BytesWritten.Should().Be(output.WrittenCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(int.MaxValue)]
    public void CharSpan_ToChars_EqualsTheStringApi(int maxOutputBytes)
    {
        var options = new JsonObserverOptions { MaxOutputBytes = maxOutputBytes };
        var output = new ArrayBufferWriter<char>();

        var result = Observer.Mask(Json.AsSpan(), output, options);

        new string(output.WrittenSpan).Should().Be(Observer.Mask(Json, out var expected, options));
        result.Should().Be(expected);
    }

    [Fact]
    public void CharSpan_LoneSurrogate_IsReplaced()
    {
        var output = new ArrayBufferWriter<char>();

        var result = JsonObserver.Obj(BlockList).Mask("{\"a\":\"x\uD800y\"}".AsSpan(), output);

        new string(output.WrittenSpan).Should().Be("{\"a\":\"x\uFFFDy\"}");
        result.Status.Should().Be(MaskStatus.Masked);
    }

    [Fact]
    public void CharSpan_WithAContext_ReadsAndMasks()
    {
        var context = new Holder();
        var observer = JsonObserver.Obj<Holder>(b => b.Match("id").ReadInt((v, c) => c.Id = v).Unmasked(), ValuePolicy.AllowList);
        var output = new ArrayBufferWriter<char>();

        observer.Mask("""{"id":7,"x":1}""".AsSpan(), output, context);
        observer.Read("""{"id":8}""".AsSpan(), context).Status.Should().Be(MaskStatus.Masked);

        new string(output.WrittenSpan).Should().Be("""{"id":7,"x":"***"}""");
        context.Id.Should().Be(8);
    }

    [Fact]
    public void NonGenericRead_ReportsWithoutWriting()
    {
        Observer.Read(Json).Should().Be(new MaskResult { Status = MaskStatus.Masked, FailedAtByte = -1 });
        Observer.Read(Json.AsSpan()).Status.Should().Be(MaskStatus.Masked);
        Observer.Read(Encoding.UTF8.GetBytes(Json)).Status.Should().Be(MaskStatus.Masked);
        Observer.Read(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("""{"a":1}{"b":2}"""))).Flags.Should().Be(MaskFlags.TrailingData);
        Observer.Read("""{"a":""").Flags.Should().Be(MaskFlags.InputTruncated);
        Observer.Read((string?)null).Status.Should().Be(MaskStatus.Unrecognized);
    }

    [Fact]
    public void FromShape_WithAContext()
    {
        var shape = JsonShape.Object(("id", JsonShape.Scalar), ("pin", JsonShape.Masked(MaskTag.Full)));
        var observer = JsonObserver.FromShape<Holder>(shape);

        observer.Mask("""{"id":1,"pin":"1234","x":2}""", new Holder()).Should().Be("""{"id":1,"pin":"***","x":"***"}""");
        observer.Explain("pin").Outcome.Should().Be(PathOutcome.Masked);
    }

    [Fact]
    public void ExistingCalls_StayUnambiguous()
    {
        Observer.Mask(null).Should().BeNull();
        Observer.Mask("""{"password":"x"}""").Should().Be("""{"password":"***"}""");
        Observer.Mask("""{"password":"x"}""", (JsonObserverOptions?)null).Should().Be("""{"password":"***"}""");
        Observer.Mask("""{"password":"x"}""", out var result).Should().Be("""{"password":"***"}""");
        result.Status.Should().Be(MaskStatus.Masked);
        Observer.Mask("""{"password":"x"}""".AsSpan(), new ArrayBufferWriter<byte>()).Status.Should().Be(MaskStatus.Masked);
        Observer.Mask("""{"password":"x"}"""u8, new ArrayBufferWriter<byte>()).Status.Should().Be(MaskStatus.Masked);
    }

    [Fact]
    public void Explain_DeclaresItsPathSyntax()
    {
        var parameter = typeof(JsonObserver).GetMethod(nameof(JsonObserver.Explain))!.GetParameters()[0];

        parameter.GetCustomAttribute<StringSyntaxAttribute>()!.Syntax.Should().Be(JsonObserver.PathSyntax).And.Be("DragoAnt.ObserverPath");
        typeof(JsonObserver<Holder>).GetMethod(nameof(JsonObserver<Holder>.Explain))!.GetParameters()[0]
            .GetCustomAttribute<StringSyntaxAttribute>()!.Syntax.Should().Be("DragoAnt.ObserverPath");
    }

    [Theory]
    [InlineData("..password")]
    [InlineData("a.*")]
    [InlineData("a[*]")]
    [InlineData("a:Last4")]
    public void Explain_AcceptsOnlyConcretePaths(string path)
        => FluentActions.Invoking(() => Observer.Explain(path)).Should().Throw<ArgumentException>();

    public sealed class Holder
    {
        public int? Id { get; set; }
    }
}
