using System.Buffers;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Buffer writer over pooled arrays. <see cref="Dispose"/> returns the array; <see cref="Reset"/> makes the instance
/// usable again, so a thread can keep one instance and allocate nothing per call.
/// </summary>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    [ThreadStatic]
    private static PooledBufferWriter? t_cached;

    private byte[] _buffer;

    public PooledBufferWriter(int initialCapacity = 1024)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    public int WrittenCount { get; private set; }

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

    /// <summary>
    /// Takes this thread's spare instance, or creates one; give it back with <see cref="Return"/>.
    /// </summary>
    public static PooledBufferWriter Rent(int initialCapacity)
    {
        var cached = t_cached;
        if (cached is null)
        {
            return new PooledBufferWriter(initialCapacity);
        }

        t_cached = null;
        cached.Reset(initialCapacity);
        return cached;
    }

    /// <summary>
    /// Returns the array to the pool and keeps the instance as this thread's spare.
    /// </summary>
    public void Return()
    {
        Dispose();
        t_cached = this;
    }

    public void Reset(int initialCapacity = 1024)
    {
        WrittenCount = 0;
        if (_buffer.Length == 0)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        }
    }

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
        WrittenCount = 0;
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
        if (_buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }

        _buffer = grown;
    }
}
