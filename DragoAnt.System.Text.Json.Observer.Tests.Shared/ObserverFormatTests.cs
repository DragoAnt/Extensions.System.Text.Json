using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Reflection;
using System.Text;
using DragoAnt.Observer.Formats;
using static DragoAnt.Observer.ValuePolicy;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class ObserverFormatTests
{
    private static readonly MaskRules Rules = MaskRules.Create(
        b => b.Match("password").Mask(MaskTag.Full).Path("card", "number").Mask(MaskTag.Last4), BlockList);
    private static readonly MaskRules[] RuleSets =
    [
        Rules,
        MaskRules.Create(b => b.Path("card", "number").Mask(MaskTag.Last4).Path("user", "ssn").Mask(MaskTag.Null), AllowList),
    ];
    private static readonly ObserverOptions?[] OptionSets =
    [
        null,
        new ObserverOptions { MaxOutputBytes = 40, HashKeyId = 3 },
        new JsonObserverOptions { Indented = true },
    ];

    [Fact]
    public void Identity_AndMarker_AreStable()
    {
        JsonObserverFormat.Instance.Name.Should().Be("json");
        JsonObserverFormat.Instance.MediaTypes.Should().Equal("application/json");
        FluentActions.Invoking(() => JsonObserverFormat.Instance.Bind(null!)).Should().Throw<ArgumentNullException>();
        var marker = typeof(JsonObserverFormat).GetCustomAttribute<ExperimentalAttribute>()!;
        marker.DiagnosticId.Should().Be("DAOEXP002");
        marker.UrlFormat.Should().Be("https://github.com/DragoAnt/Observer/blob/main/docs/experimental.md");
    }

    [Fact]
    public void BaseOptions_AreMappedWithoutLosingProperties()
    {
        var source = new ObserverOptions();
        var properties = typeof(ObserverOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod?.IsPublic == true)
            .ToArray();
        foreach (var property in properties)
        {
            var value = NonDefaultValue(property.PropertyType, property.GetValue(source));
            value.Should().NotBe(property.GetValue(source), property.Name);
            property.SetValue(source, value);
        }

        var mapped = JsonObserverOptions.From(source);
        foreach (var property in properties)
            property.GetValue(mapped).Should().Be(property.GetValue(source), property.Name);
        mapped.RelaxedEscaping.Should().BeTrue();
        mapped.Indented.Should().BeFalse();
        JsonObserverOptions.From(mapped).Should().BeSameAs(mapped);
    }

    private static object NonDefaultValue(Type type, object? current) => type switch
    {
        _ when type == typeof(int) => 7,
        _ when type == typeof(bool) => !(bool)current!,
        _ when type == typeof(int?) => (int?)9,
        _ when type == typeof(ReadOnlyMemory<byte>) => new ReadOnlyMemory<byte>(new byte[64]),
        _ when type == typeof(ValueMaskStrategy) => new TestStrategy(),
        _ when type == typeof(CommentPolicy) => CommentPolicy.BlockList,
        _ => throw new InvalidOperationException($"No non-default test value for {type}.")
    };

    private sealed class TestStrategy : ValueMaskStrategy
    {
        public override void Mask(in MaskContext context, MaskValueWriter output) => output.Null();
    }

    [Fact]
    public void Sync_EqualsFromRules()
    {
        foreach (var rules in RuleSets)
        foreach (var options in OptionSets)
        foreach (var document in ParityCorpus.Documents)
        {
            var binding = JsonObserverFormat.Instance.Bind(rules);
            var observer = JsonObserver.From(rules);
            var input = Encoding.UTF8.GetBytes(document);
            var actual = new ArrayBufferWriter<byte>();
            var expected = new ArrayBufferWriter<byte>();
            var actualResult = binding.Mask(input, actual, options);
            var expectedResult = observer.Mask(input, expected, JsonObserverOptions.From(options));
            actual.WrittenSpan.ToArray().Should().Equal(expected.WrittenSpan.ToArray());
            actualResult.Should().Be(expectedResult);
        }
    }

    [Fact]
    public async Task Async_EqualsSync_AndLeavesPipesOpen()
    {
        var binding = JsonObserverFormat.Instance.Bind(Rules);
        foreach (var document in ParityCorpus.Documents)
        {
            var bytes = Encoding.UTF8.GetBytes(document);
            var syncOutput = new ArrayBufferWriter<byte>();
            var sync = binding.Mask(bytes, syncOutput);
            await using var inputStream = new MemoryStream(bytes);
            await using var outputStream = new MemoryStream();
            var reader = PipeReader.Create(inputStream, new StreamPipeReaderOptions(bufferSize: 1, minimumReadSize: 1, leaveOpen: true));
            var writer = PipeWriter.Create(outputStream, new StreamPipeWriterOptions(leaveOpen: true));

            var result = await binding.MaskAsync(reader, writer);

            result.Should().Be(MaskRecordsResult.FromDocument(sync, bytes.Length));
            outputStream.ToArray().Should().Equal(syncOutput.WrittenSpan.ToArray());
            await writer.WriteAsync("x"u8.ToArray());
            var read = await reader.ReadAsync();
            reader.AdvanceTo(read.Buffer.End);
            await reader.CompleteAsync();
            await writer.CompleteAsync();
        }
    }

    [Fact]
    public async Task Async_Cancellation_IsReported()
    {
        var binding = JsonObserverFormat.Instance.Bind(Rules);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var reader = PipeReader.Create(new MemoryStream());
        var writer = PipeWriter.Create(new MemoryStream());
        await FluentActions.Awaiting(async () => await binding.MaskAsync(reader, writer, cancellationToken: cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await reader.CompleteAsync();
        await writer.CompleteAsync();

        reader = PipeReader.Create(new MemoryStream("{}"u8.ToArray()));
        writer = PipeWriter.Create(new MemoryStream());
        reader.CancelPendingRead();
        await FluentActions.Awaiting(async () => await binding.MaskAsync(reader, writer))
            .Should().ThrowAsync<OperationCanceledException>();
        await reader.CompleteAsync();
        await writer.CompleteAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_InputOverTheDocumentLimit_IsTruncatedAndTheRestLeftUnread(bool completed)
    {
        var binding = JsonObserverFormat.Instance.Bind(Rules);
        var reader = new OversizedReader(completed);
        await using var stream = new MemoryStream();
        var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await binding.MaskAsync(reader, writer, new ObserverOptions { MaxOutputBytes = 1024 }, cts.Token);

        result.Status.Should().Be(MaskStatus.Truncated);
        result.Flags.Should().HaveFlag(MaskFlags.InputTruncated);
        result.Records.Should().Be(1);
        result.FailedRecords.Should().Be(0);
        result.BytesRead.Should().Be(int.MaxValue);
        reader.ConsumedBytes.Should().Be(int.MaxValue);
        result.BytesWritten.Should().Be(stream.Length);
        Encoding.UTF8.GetString(stream.ToArray()).Should().NotContain("xxxxxxxx");
        await writer.CompleteAsync();
    }

    private sealed class OversizedReader(bool completed) : PipeReader
    {
        private const int SegmentBytes = 1 << 20;
        private readonly ReadOnlySequence<byte> _buffer = Create((long)int.MaxValue + SegmentBytes);

        public long ConsumedBytes { get; private set; } = -1;

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(new ReadResult(_buffer, isCanceled: false, isCompleted: completed));
        }

        public override bool TryRead(out ReadResult result)
        {
            result = new ReadResult(_buffer, isCanceled: false, isCompleted: completed);
            return true;
        }

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
            => ConsumedBytes = _buffer.Slice(_buffer.Start, consumed).Length;

        public override void CancelPendingRead() { }

        public override void Complete(Exception? exception = null) { }

        private static ReadOnlySequence<byte> Create(long length)
        {
            var filler = new byte[SegmentBytes];
            filler.AsSpan().Fill((byte)'x');
            var first = new Segment("{\"password\":\""u8.ToArray(), 0);
            var last = first;
            while (last.RunningIndex + last.Memory.Length < length)
            {
                var size = (int)Math.Min(SegmentBytes, length - last.RunningIndex - last.Memory.Length);
                last = last.Append(filler.AsMemory(0, size));
            }

            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
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

    [Fact]
    public async Task Async_RealPipe_WithAFlushingWriter_CompletesForALargeDocument()
    {
        var document = LargeDocument(1 << 20);
        var pipe = new Pipe();
        await using var stream = new MemoryStream();
        var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var producer = ProduceAsync(pipe.Writer, document, cts.Token);

        var result = await JsonObserverFormat.Instance.Bind(Rules).MaskAsync(pipe.Reader, writer, cancellationToken: cts.Token);
        await producer;

        result.Status.Should().Be(MaskStatus.Masked);
        result.BytesRead.Should().Be(document.Length);
        var expected = new ArrayBufferWriter<byte>();
        JsonObserver.From(Rules).Mask(document, expected);
        stream.ToArray().Should().Equal(expected.WrittenSpan.ToArray());
        await pipe.Reader.CompleteAsync();
        await writer.CompleteAsync();
    }

    [Fact]
    public async Task Async_RealPipe_OverTheDocumentLimit_IsTruncatedAndTheRestLeftUnread()
    {
        const int limit = 100_000;
        var document = LargeDocument(1 << 20);
        var pipe = new Pipe();
        await using var stream = new MemoryStream();
        var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var producer = ProduceAsync(pipe.Writer, document, cts.Token);

        var result = await JsonObserverFormat.Bind(Rules, limit).MaskAsync(pipe.Reader, writer, cancellationToken: cts.Token);

        result.Status.Should().Be(MaskStatus.Truncated);
        result.Flags.Should().HaveFlag(MaskFlags.InputTruncated);
        result.BytesRead.Should().Be(limit);
        Encoding.UTF8.GetString(stream.ToArray()).Should().NotContain("s3cr3t-7f2a");
        long rest = 0;
        while (true)
        {
            var read = await pipe.Reader.ReadAsync(cts.Token);
            rest += read.Buffer.Length;
            pipe.Reader.AdvanceTo(read.Buffer.End);
            if (read.IsCompleted)
                break;
        }

        await producer;
        rest.Should().Be(document.Length - limit);
        await pipe.Reader.CompleteAsync();
        await writer.CompleteAsync();
    }

    private static byte[] LargeDocument(int minBytes)
    {
        var builder = new StringBuilder("{\"password\":\"s3cr3t-7f2a\",\"items\":[");
        for (var i = 0; builder.Length < minBytes; i++)
            builder.Append(i == 0 ? "" : ",").Append("{\"id\":").Append(i).Append(",\"name\":\"item-").Append(i).Append("\"}");
        builder.Append("]}");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static async Task ProduceAsync(PipeWriter writer, byte[] document, CancellationToken cancellationToken)
    {
        await Task.Yield();
        for (var offset = 0; offset < document.Length; offset += 4096)
        {
            await writer.WriteAsync(document.AsMemory(offset, Math.Min(4096, document.Length - offset)), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }

        await writer.CompleteAsync();
    }

    [Fact]
    public async Task FailedDocument_IsARunOfOne()
    {
        var bytes = "\"password\""u8.ToArray();
        var binding = JsonObserverFormat.Instance.Bind(Rules);
        var reader = PipeReader.Create(new MemoryStream(bytes));
        await using var stream = new MemoryStream();
        var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        var result = await binding.MaskAsync(reader, writer);
        result.Status.Should().Be(MaskStatus.Unrecognized);
        result.Flags.Should().HaveFlag(MaskFlags.RecordFailed);
        result.Records.Should().Be(1);
        result.FailedRecords.Should().Be(1);
        result.FirstFailedRecord.Should().Be(0);
        result.BytesWritten.Should().Be(0);
        await reader.CompleteAsync();
        await writer.CompleteAsync();
    }
}
