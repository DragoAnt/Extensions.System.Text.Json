using System.Buffers;
using System.Runtime.ExceptionServices;

namespace DragoAnt.System.Text.Json.Observer.Http;

internal enum CaptureEnd
{
    EndOfBody,
    LimitReached,
    Failed,
    Stopped,
}

/// <summary>
/// Reads the start of a response body in the background while the caller reads the same bytes through <see cref="CreateContent"/>.
/// The pooled buffer is owned by this object and returned only after both the background read and the caller are done with it.
/// </summary>
internal sealed class ResponseBodyCapture
{
    internal delegate void Completed(ReadOnlySpan<byte> prefix, bool truncated, CaptureEnd end);

    private readonly object _gate = new();
    private readonly HttpContent _original;
    private readonly Stream _source;
    private readonly int _limit;
    private readonly int _target;
    private readonly Completed _completed;
    private readonly CancellationTokenSource _stop = new();
    private byte[] _buffer;
    private int _captured;
    private int _position;
    private CaptureEnd? _end;
    private ExceptionDispatchInfo? _error;
    private TaskCompletionSource _progress = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _owners = 2;
    private int _stopped;

    public ResponseBodyCapture(HttpContent original, Stream source, int limit, Completed completed)
    {
        _original = original;
        _source = source;
        _limit = limit;
        _target = limit + 1;
        _completed = completed;
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Min(_target, 4096));
    }

    public HttpContent CreateContent()
    {
        var content = new StreamContent(new CaptureStream(this));
        foreach (var header in _original.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return content;
    }

    public void Start() => _ = Task.Run(PumpAsync);

    private async Task PumpAsync()
    {
        var end = CaptureEnd.EndOfBody;
        ExceptionDispatchInfo? error = null;
        try
        {
            while (true)
            {
                if (_captured == _target)
                {
                    end = CaptureEnd.LimitReached;
                    break;
                }

                EnsureCapacity();
                var read = await _source
                    .ReadAsync(_buffer.AsMemory(_captured, Math.Min(_buffer.Length, _target) - _captured), _stop.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                Publish(read);
            }
        }
        catch (Exception ex)
        {
            end = _stop.IsCancellationRequested ? CaptureEnd.Stopped : CaptureEnd.Failed;
            error = ExceptionDispatchInfo.Capture(ex);
        }

        try
        {
            _completed(_buffer.AsSpan(0, Math.Min(_captured, _limit)), _captured > _limit, end);
        }
        finally
        {
            TaskCompletionSource progress;
            lock (_gate)
            {
                _end = end;
                _error = error;
                progress = _progress;
            }

            progress.TrySetResult();
            Release();
        }
    }

    private void EnsureCapacity()
    {
        if (_captured < _buffer.Length)
        {
            return;
        }

        var grown = ArrayPool<byte>.Shared.Rent((int)Math.Min(_target, (long)_buffer.Length * 2));
        byte[] old;
        lock (_gate)
        {
            _buffer.AsSpan(0, _captured).CopyTo(grown);
            old = _buffer;
            _buffer = grown;
        }

        ArrayPool<byte>.Shared.Return(old, clearArray: true);
    }

    private void Publish(int read)
    {
        TaskCompletionSource progress;
        lock (_gate)
        {
            _captured += read;
            progress = _progress;
            _progress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        progress.TrySetResult();
    }

    private bool TryReadCaptured(Span<byte> destination, out int count, out Task? wait, out bool readSource)
    {
        lock (_gate)
        {
            readSource = false;
            wait = null;
            count = 0;
            if (_stopped != 0)
            {
                throw new ObjectDisposedException(nameof(HttpContent));
            }

            if (_position < _captured)
            {
                count = Math.Min(destination.Length, _captured - _position);
                _buffer.AsSpan(_position, count).CopyTo(destination);
                _position += count;
                return true;
            }

            switch (_end)
            {
                case CaptureEnd.EndOfBody:
                    return true;
                case CaptureEnd.LimitReached:
                    readSource = true;
                    return true;
                case CaptureEnd.Failed:
                    _error!.Throw();
                    return true;
                case CaptureEnd.Stopped:
                    throw new ObjectDisposedException(nameof(HttpContent));
                default:
                    wait = _progress.Task;
                    return false;
            }
        }
    }

    private async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        while (true)
        {
            if (TryReadCaptured(destination.Span, out var count, out var wait, out var readSource))
            {
                return readSource ? await _source.ReadAsync(destination, cancellationToken).ConfigureAwait(false) : count;
            }

            await wait!.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private int Read(Span<byte> destination)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        while (true)
        {
            if (TryReadCaptured(destination, out var count, out var wait, out var readSource))
            {
                return readSource ? _source.Read(destination) : count;
            }

            wait!.GetAwaiter().GetResult();
        }
    }

    private void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        try
        {
            _stop.Cancel();
            _source.Dispose();
            _original.Dispose();
        }
        finally
        {
            Release();
        }
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _owners) != 0)
        {
            return;
        }

        byte[] buffer;
        lock (_gate)
        {
            buffer = _buffer;
            _buffer = [];
        }

        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        _stop.Dispose();
    }

    private sealed class CaptureStream(ResponseBodyCapture capture) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return capture.Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer) => capture.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            capture.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return capture.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                capture.Stop();
            }

            base.Dispose(disposing);
        }
    }
}
