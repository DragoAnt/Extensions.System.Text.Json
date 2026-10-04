using System.Buffers;
using System.Text;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

/// <summary>
/// Every masking case replayed as a multi-segment sequence: a name, string or number split across segments must be
/// handled exactly like the same bytes in one span.
/// </summary>
public abstract class SequenceInputTests
{
    private static readonly PropMatchingStrategy AnyItem = new(_ => true);

    private static readonly JsonObserver Tags = JsonObserver.Obj(Relative(b => b
            .Match("full").MaskAny(MaskTag.Full)
            .Match("last4").MaskAny(MaskTag.Last4)
            .Match("hash").MaskAny(MaskTag.Hash)
            .Match("omit").MaskAny(MaskKind.Omit),
        BlockList));

    private static readonly JsonObserver Nested = JsonObserver.Obj(
        root => root.Match("lines").Array(l => l.Obj(x => x.Match("qty").MaskAny("***").Match("sku").MaskStr((v, _) => v + "!"))),
        Relative(b => b.Match("lines", AnyItem, "note").MaskRawValue((v, _) => "<" + v + ">"), BlockList));

    private static readonly JsonObserver NonAscii = JsonObserver.Obj(Relative(b => b
            .Match("пароль").MaskAny("1")
            .Match(PropMatches.Contains("ключ")).MaskInt((v, _) => $"{v}")
            .Match("password").MaskAny("2"),
        AllowList));

    private static readonly Dictionary<string, (JsonObserver Observer, string Json, JsonObserverOptions? Options)> Cases = BuildCases();

    private static Dictionary<string, (JsonObserver, string, JsonObserverOptions?)> BuildCases()
    {
        var cases = new Dictionary<string, (JsonObserver, string, JsonObserverOptions?)>
        {
            ["golden-request"] = (JsonMaskingTests.GetRequestMasking(BlockList), JsonMaskingTests.TestJson, null),
            ["golden-ignore-nulls"] = (JsonMaskingTests.GetRequestUnmasking(NullList), JsonMaskingTests.TestJson, new JsonObserverOptions(IgnoreNulls: true, Indented: true)),
            ["tags"] = (Tags, """{"full":{"a":[1,2]},"last4":"4111111111111111","hash":"S3cr3t","omit":12.5e3,"x":"y"}""", null),
            ["shape"] = (JsonShapeTests.Observer(), """{"id":1,"orders":[{"sku":"A1","secretCode":"x","extra":1}],"byCode":{"K1":{"sku":"B"}},"card":"4111111111111111","e_mail":"a@b.c","unknown":{"deep":true}}""", null),
            ["nested-array"] = (Nested, """{"lines":[{"qty":5,"sku":"A","note":"n\"1"},{"qty":-7.25,"sku":"Bé"}],"total":12}""", null),
            ["non-ascii"] = (NonAscii, """{"пароль":"x","мой ключ":42,"password":"y","Ж":"z"}""", null),
            ["escaped-long"] = (BytesApiTests.Observer, "{\"password\":\"" + new string('s', 300) + "\\n\\u00e9\",\"n" + new string('m', 300) + "\":" + new string('9', 40) + "}", null),
            ["bom"] = (BytesApiTests.Observer, "﻿{\"password\":\"x\",\"ok\":true}", null),
            ["truncated"] = (BytesApiTests.Observer, """{"user":"bob","password":"S3cr3t","card":{"pin":"123""", null),
            ["invalid"] = (BytesApiTests.Observer, """{"user":"bob",,"password":"x"}""", null),
            ["not-json"] = (BytesApiTests.Observer, "   42", null),
            ["max-output"] = (BytesApiTests.Observer, BytesApiTests.Payloads[0], new JsonObserverOptions(MaxOutputBytes: 60)),
            ["max-value"] = (BytesApiTests.Observer, """{"text":"éééééééé","password":"x"}""", new JsonObserverOptions(MaxValueBytes: 5)),
            ["case-sensitive"] = (NonAscii, """{"PASSWORD":"x","password":"y"}""", new JsonObserverOptions(PropertyNameCaseInsensitive: false)),
            ["nested-17"] = (BytesApiTests.Observer, NestingTests.Nested(17).Replace("password", "pin", StringComparison.Ordinal), null),
        };

        for (var i = 0; i < BytesApiTests.Payloads.Length; i++)
        {
            cases[$"bytes-payload-{i}"] = (BytesApiTests.Observer, BytesApiTests.Payloads[i], null);
        }

        foreach (var shape in new[] { "flat", "nested", "array" })
        {
            cases[$"allocation-{shape}"] = (BytesApiTests.Observer, AllocationTests.Payload(shape, 2048), null);
        }

        return cases;
    }

    public static TheoryData<string, int> Replays()
    {
        var data = new TheoryData<string, int>();
        foreach (var name in Cases.Keys)
        {
            data.Add(name, 1);
            data.Add(name, 3);
            data.Add(name, 7);
        }

        return data;
    }

    internal static ReadOnlySequence<byte> Split(byte[] utf8, int segmentSize)
    {
        if (utf8.Length == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        Segment? first = null;
        Segment? last = null;
        for (var offset = 0; offset < utf8.Length; offset += segmentSize)
        {
            var memory = utf8.AsMemory(offset, Math.Min(segmentSize, utf8.Length - offset));
            last = last is null ? first = new Segment(memory, 0) : last.Append(memory);
        }

        return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
    }

    [Theory]
    [MemberData(nameof(Replays))]
    public void Mask_SplitIntoSegments_SameAsSpan(string name, int segmentSize)
    {
        var (observer, json, options) = Cases[name];
        var utf8 = Encoding.UTF8.GetBytes(json);
        var spanOutput = new ArrayBufferWriter<byte>();
        var sequenceOutput = new ArrayBufferWriter<byte>();

        var spanResult = observer.Mask(utf8, spanOutput, options);
        var sequenceResult = observer.Mask(Split(utf8, segmentSize), sequenceOutput, options);

        sequenceResult.Should().Be(spanResult, name);
        Encoding.UTF8.GetString(sequenceOutput.WrittenSpan).Should().Be(Encoding.UTF8.GetString(spanOutput.WrittenSpan), name);
    }

    [Fact]
    public void Mask_SingleSegmentAndEmpty_SameAsSpan()
    {
        var utf8 = Encoding.UTF8.GetBytes(BytesApiTests.Payloads[0]);
        var spanOutput = new ArrayBufferWriter<byte>();
        var sequenceOutput = new ArrayBufferWriter<byte>();

        BytesApiTests.Observer.Mask(new ReadOnlySequence<byte>(utf8), sequenceOutput)
            .Should().Be(BytesApiTests.Observer.Mask(utf8, spanOutput));
        sequenceOutput.WrittenSpan.SequenceEqual(spanOutput.WrittenSpan).Should().BeTrue();
        BytesApiTests.Observer.Mask(ReadOnlySequence<byte>.Empty, new ArrayBufferWriter<byte>())
            .Should().Be(new MaskResult(MaskStatus.NotJson, 0, 0));
        BytesApiTests.Observer.Mask(Split([0xEF, 0xBB], 1), new ArrayBufferWriter<byte>())
            .Should().Be(BytesApiTests.Observer.Mask([0xEF, 0xBB], new ArrayBufferWriter<byte>()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Read_SplitIntoSegments_ReadsSameValues(int segmentSize)
    {
        var observer = JsonObserver.Obj<Extracted>(ReadRules(b => b
            .Match("id").ReadStr((v, c) => c.Values.Add($"id={v}"))
            .Match("n").ReadDecimal((v, c) => c.Values.Add($"n={v}"))
            .Match("raw").ReadRaw((v, c) => c.Values.Add($"raw={v}"))
            .Match("ok").ReadBool((v, c) => c.Values.Add($"ok={v}"))));
        var utf8 = Encoding.UTF8.GetBytes("""{"a":[{"id":"xé-1","n":12345.678},{"raw":"r\"q","ok":true}],"id":""" + "\"" + new string('z', 50) + "\"}");
        var fromSpan = new Extracted();
        var fromSequence = new Extracted();

        var spanResult = observer.Read(utf8, fromSpan);
        var sequenceResult = observer.Read(Split(utf8, segmentSize), fromSequence);

        sequenceResult.Should().Be(spanResult);
        fromSequence.Values.Should().Equal(fromSpan.Values);
        fromSpan.Values.Should().HaveCount(5);
    }

    [Fact]
    public void Read_SingleSegment_UsesSpanPath()
    {
        var context = new Extracted();
        var observer = JsonObserver.Obj<Extracted>(ReadRules(b => b.Match("id").ReadStr((v, c) => c.Values.Add(v!))));

        observer.Read(new ReadOnlySequence<byte>("""{"id":"a"}"""u8.ToArray()), context).Status.Should().Be(MaskStatus.Masked);
        context.Values.Should().Equal("a");
    }

    private static JsonObserverValueDelegate<Extracted> ReadRules(Action<Builders.JsonValuePolicyBuilder<Extracted>> init) =>
        JsonObserverValuePolicies<Extracted>.Relative(init, JsonObserverValuePolicies<Extracted>.BlockList);

    public sealed class Extracted
    {
        public List<string> Values { get; } = [];
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
