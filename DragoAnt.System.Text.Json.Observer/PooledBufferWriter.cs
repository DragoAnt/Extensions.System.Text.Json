using System.Buffers;

namespace DragoAnt.System.Text.Json.Observer;

internal sealed class PooledBufferWriter(int initialCapacity = 1024) : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);

    public int WrittenCount { get; private set; }

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

    public void Advance(int count) => WrittenCount += count;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(WrittenCount);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(WrittenCount);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = [];
        if (buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Ensure(int sizeHint)
    {
        var required = WrittenCount + Math.Max(sizeHint, 1);
        if (required <= _buffer.Length)
        {
            return;
        }

        var grown = ArrayPool<byte>.Shared.Rent(Math.Max(required, _buffer.Length * 2));
        WrittenSpan.CopyTo(grown);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = grown;
    }
}
