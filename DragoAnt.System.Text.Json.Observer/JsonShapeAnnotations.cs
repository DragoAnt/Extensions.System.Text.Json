namespace DragoAnt.System.Text.Json.Observer;

/// <summary>
/// Data an integration attaches to a <see cref="JsonShape"/> node or a <see cref="JsonShapeProperty"/>, one value per
/// type, for example validation rules or a data classification. Annotations never change masking; they can be set at
/// any time and are safe to read and write from several threads.
/// </summary>
public sealed class JsonShapeAnnotations
{
    private readonly object _sync = new();
    private (Type Type, object Value)[] _values = [];

    internal JsonShapeAnnotations()
    {
    }

    /// <summary>
    /// Number of annotations.
    /// </summary>
    public int Count => Volatile.Read(ref _values).Length;

    /// <summary>
    /// Sets the annotation of type <typeparamref name="T"/>, replacing any previous one.
    /// </summary>
    /// <param name="value">The annotation.</param>
    /// <typeparam name="T">Type the annotation is stored under.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
    public void Set<T>(T value)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            var values = _values;
            var index = IndexOf(values, typeof(T));
            var updated = new (Type Type, object Value)[index >= 0 ? values.Length : values.Length + 1];
            values.CopyTo(updated, 0);
            updated[index >= 0 ? index : values.Length] = (typeof(T), value);
            Volatile.Write(ref _values, updated);
        }
    }

    /// <summary>
    /// Gets the annotation of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="value">The annotation; <c>default</c> when there is none.</param>
    /// <typeparam name="T">Type the annotation is stored under.</typeparam>
    /// <returns><c>true</c> when there is one.</returns>
    public bool TryGet<T>(out T? value)
    {
        var values = Volatile.Read(ref _values);
        var index = IndexOf(values, typeof(T));
        value = index >= 0 ? (T)values[index].Value : default;
        return index >= 0;
    }

    /// <summary>
    /// Gets the annotation of type <typeparamref name="T"/>, or <c>default</c> when there is none.
    /// </summary>
    /// <typeparam name="T">Type the annotation is stored under.</typeparam>
    public T? Get<T>() => TryGet<T>(out var value) ? value : default;

    /// <summary>
    /// Removes the annotation of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Type the annotation is stored under.</typeparam>
    /// <returns><c>true</c> when there was one.</returns>
    public bool Remove<T>()
    {
        lock (_sync)
        {
            var values = _values;
            var index = IndexOf(values, typeof(T));
            if (index < 0)
            {
                return false;
            }

            Volatile.Write(ref _values, [.. values[..index], .. values[(index + 1)..]]);
            return true;
        }
    }

    private static int IndexOf((Type Type, object Value)[] values, Type type)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].Type == type)
            {
                return i;
            }
        }

        return -1;
    }
}
