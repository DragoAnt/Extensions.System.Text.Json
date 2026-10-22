using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using DragoAnt.Observer.Formats;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// JSON as an <see cref="IObserverFormat"/>: <c>json</c>, <c>application/json</c>. A binding masks like
/// <see cref="JsonObserver.From(MaskRules)"/>; pass <see cref="JsonObserverOptions"/> for the JSON output settings.
/// <c>MaskAsync</c> holds the whole document in memory before masking it, up to <see cref="int.MaxValue"/> bytes:
/// a longer input is masked up to that length, reported as <see cref="MaskStatus.Truncated"/> with
/// <see cref="MaskFlags.InputTruncated"/>, and the rest is left unread in the reader.
/// </summary>
[Experimental("DAOEXP002", UrlFormat = "https://github.com/DragoAnt/Observer/blob/main/docs/experimental.md")]
public sealed class JsonObserverFormat : IObserverFormat
{
    private const long MaxDocumentBytes = int.MaxValue;

    private JsonObserverFormat() { }

    /// <summary>The format.</summary>
    public static JsonObserverFormat Instance { get; } = new();

    /// <inheritdoc />
    public string Name => "json";

    /// <inheritdoc />
    public IReadOnlyList<string> MediaTypes { get; } = ["application/json"];

    /// <inheritdoc />
    public IObserverFormatBinding Bind(MaskRules rules)
        => Bind(rules, MaxDocumentBytes);

    internal static IObserverFormatBinding Bind(MaskRules rules, long maxDocumentBytes)
        => new Binding(JsonObserver.From(rules ?? throw new ArgumentNullException(nameof(rules))), maxDocumentBytes);

    private sealed class Binding(JsonObserver observer, long maxDocumentBytes) : IObserverFormatBinding
    {
        public MaskResult Mask(ReadOnlySpan<byte> input, IBufferWriter<byte> output, ObserverOptions? options = null)
            => observer.Mask(input, output, JsonObserverOptions.From(options));

        public async ValueTask<MaskRecordsResult> MaskAsync(PipeReader input, PipeWriter output,
            ObserverOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(output);
            cancellationToken.ThrowIfCancellationRequested();

            while (true)
            {
                var read = await input.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (read.IsCanceled)
                {
                    input.AdvanceTo(read.Buffer.Start, read.Buffer.End);
                    throw new OperationCanceledException(cancellationToken);
                }

                var oversized = read.Buffer.Length > maxDocumentBytes;
                if (!read.IsCompleted && !oversized)
                {
                    input.AdvanceTo(read.Buffer.Start, read.Buffer.End);
                    continue;
                }

                var document = oversized ? read.Buffer.Slice(0, maxDocumentBytes) : read.Buffer;
                var length = document.Length;
                MaskResult result;
                try
                {
                    result = observer.Mask(document, output, JsonObserverOptions.From(options));
                }
                finally
                {
                    input.AdvanceTo(document.End, read.Buffer.End);
                }

                if (oversized && result.Status is MaskStatus.Masked or MaskStatus.Truncated)
                {
                    result = result with
                    {
                        Status = MaskStatus.Truncated,
                        Flags = result.Flags | MaskFlags.InputTruncated,
                        FailedAtByte = result.FailedAtByte < 0 ? maxDocumentBytes : result.FailedAtByte,
                    };
                }

                var flush = await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (flush.IsCanceled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                return MaskRecordsResult.FromDocument(result, length);
            }
        }
    }
}
