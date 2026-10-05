using System.Buffers;
using System.Text;

namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Comments read but not yet written: those before a member wait until its rule is known.
/// </summary>
internal sealed class CommentBuffer
{
    [ThreadStatic]
    private static CommentBuffer? t_cached;

    private byte[] _bytes = [];
    private Entry[] _entries = [];
    private int _used;

    public int Count { get; private set; }

    public static CommentBuffer Rent()
    {
        var buffer = t_cached ?? new CommentBuffer();
        t_cached = null;
        return buffer;
    }

    public void Return()
    {
        Clear();
        if (_bytes.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_bytes, clearArray: true);
            _bytes = [];
        }

        t_cached = this;
    }

    public void Add(ref Utf8JsonReader reader, CommentStyle style)
    {
        var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        if (_used + length > _bytes.Length)
        {
            var grown = ArrayPool<byte>.Shared.Rent(Math.Max(_used + length, Math.Max(_bytes.Length * 2, 256)));
            _bytes.AsSpan(0, _used).CopyTo(grown);
            if (_bytes.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(_bytes, clearArray: true);
            }

            _bytes = grown;
        }

        if (reader.HasValueSequence)
        {
            reader.ValueSequence.CopyTo(_bytes.AsSpan(_used));
        }
        else
        {
            reader.ValueSpan.CopyTo(_bytes.AsSpan(_used));
        }

        if (Count == _entries.Length)
        {
            global::System.Array.Resize(ref _entries, Math.Max(4, _entries.Length * 2));
        }

        _entries[Count++] = new Entry(_used, length, style);
        _used += length;
    }

    public ReadOnlySpan<byte> Text(int index) => _bytes.AsSpan(_entries[index].Start, _entries[index].Length);

    public CommentStyle Style(int index) => _entries[index].Style;

    public void Clear()
    {
        Count = 0;
        _used = 0;
    }

    public void Truncate(int count)
    {
        _used = count == 0 ? 0 : _entries[count].Start;
        Count = count;
    }

    private readonly record struct Entry(int Start, int Length, CommentStyle Style);
}

/// <summary>
/// Decides and writes comments for the JSON walkers, following the call's <see cref="CommentPolicy"/> and the comment
/// rules of the owners' path rules.
/// </summary>
internal static class JsonComments
{
    /// <summary>
    /// Handles a comment token inside a container: written at once as <see cref="CommentKind.Inline"/> when it stands on
    /// the line of the value just written (whose member is still on the path), otherwise kept for the next member.
    /// </summary>
    public static void OnComment<TContext>(
        ref Utf8JsonReader reader,
        JsonWriter writer,
        ref JsonWalk walk,
        bool previousOpen,
        JsonObserverItem<TContext>? previousItem,
        bool previousMasked)
    {
        var style = walk.StyleAt(reader.TokenStartIndex);
        if (previousOpen && walk.IsSameLine(walk.LastValueEnd, reader.TokenStartIndex))
        {
            var pending = walk.Pending!;
            var mark = pending.Count;
            pending.Add(ref reader, style);
            Write(pending, mark, writer, ref walk, CommentKind.Inline, previousItem?.CommentRuleFor(CommentKind.Inline), previousMasked);
            return;
        }

        walk.Pending!.Add(ref reader, style);
    }

    /// <summary>
    /// Writes and clears the kept comments as <paramref name="kind"/> of the member at the end of the path.
    /// </summary>
    public static void Flush(JsonWriter writer, ref JsonWalk walk, CommentKind kind, CommentRule? rule, bool ownerMasked)
    {
        var pending = walk.Pending;
        if (pending is null || pending.Count == 0)
        {
            return;
        }

        Write(pending, 0, writer, ref walk, kind, rule, ownerMasked);
    }

    private static void Write(CommentBuffer pending, int from, JsonWriter writer, ref JsonWalk walk, CommentKind kind, CommentRule? rule, bool ownerMasked)
    {
        var policy = walk.Comments!;
        for (var i = from; i < pending.Count; i++)
        {
            var text = pending.Text(i);
            var context = new CommentContext(text, kind, pending.Style(i), in walk.Path, ownerMasked);
            policy.Decide(ref context, rule);
            switch (context.Action)
            {
                case CommentAction.Keep:
                case CommentAction.Raw:
                    writer.WriteComment(text);
                    break;
                case CommentAction.Mask:
                    WriteMasked(text, context.MaskTag, writer, ref walk);
                    break;
                case CommentAction.Replace:
                    WriteReplacement(context.Replacement ?? string.Empty, writer);
                    break;
            }
        }

        if (from == 0)
        {
            pending.Clear();
        }
        else
        {
            pending.Truncate(from);
        }
    }

    private static void WriteReplacement(string replacement, JsonWriter writer)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(replacement.Length));
        try
        {
            writer.WriteComment(bytes.AsSpan(0, Encoding.UTF8.GetBytes(replacement, bytes)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes, clearArray: true);
        }
    }

    private static void WriteMasked(ReadOnlySpan<byte> text, MaskTag tag, JsonWriter writer, ref JsonWalk walk)
    {
        var options = walk.Options;
        var strategy = options.Strategy ?? ValueMaskStrategy.Default;
        var output = CommentTextWriter.Rent(text);
        try
        {
            strategy.Mask(new MaskContext(text, ValueKind.String, tag, options, in walk.Path, walk.ValueIndex++), output);
            if (output.HasText)
            {
                writer.WriteComment(output.Text);
            }
        }
        finally
        {
            output.Return();
        }
    }

    /// <summary>
    /// Collects a strategy's replacement of a comment's text.
    /// </summary>
    private sealed class CommentTextWriter : MaskValueWriter
    {
        [ThreadStatic]
        private static CommentTextWriter? t_cached;

        private byte[] _buffer = [];
        private byte[] _original = [];
        private int _length;
        private int _originalLength;

        public bool HasText { get; private set; }

        public ReadOnlySpan<byte> Text => _buffer.AsSpan(0, _length);

        public static CommentTextWriter Rent(ReadOnlySpan<byte> original)
        {
            var writer = t_cached ?? new CommentTextWriter();
            t_cached = null;
            writer.HasText = false;
            writer._length = 0;
            writer._originalLength = original.Length;
            writer.Ensure(ref writer._original, original.Length);
            original.CopyTo(writer._original);
            return writer;
        }

        public void Return()
        {
            _buffer.AsSpan(0, _length).Clear();
            _original.AsSpan(0, _originalLength).Clear();
            t_cached = this;
        }

        public override void String(ReadOnlySpan<byte> utf8) => Set(utf8);

        public override void String(ReadOnlySpan<char> chars)
        {
            Ensure(ref _buffer, Encoding.UTF8.GetMaxByteCount(chars.Length));
            _length = Encoding.UTF8.GetBytes(chars, _buffer);
            HasText = true;
        }

        public override void Boolean(bool value) => Set(value ? "true"u8 : "false"u8);

        public override void Null() => HasText = false;

        public override void Keep() => Set(_original.AsSpan(0, _originalLength));

        protected override void WriteNumber(ReadOnlySpan<byte> utf8Literal) => Set(utf8Literal);

        private void Set(ReadOnlySpan<byte> utf8)
        {
            Ensure(ref _buffer, utf8.Length);
            utf8.CopyTo(_buffer);
            _length = utf8.Length;
            HasText = true;
        }

        private void Ensure(ref byte[] buffer, int length)
        {
            if (buffer.Length < length)
            {
                buffer = new byte[Math.Max(length, 64)];
            }
        }
    }
}
