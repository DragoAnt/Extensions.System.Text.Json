using System.Buffers;

namespace Extensions.System.Text.Json.Observer;

/// <summary>
/// JSON property path.
/// </summary>
public ref struct PropertyPath(int capacity)
{
    private string?[] _path = ArrayPool<string?>.Shared.Rent(capacity);

    /// <summary>
    /// Current property path depth.
    /// </summary>
    private int Depth { get; set; } = -1;

    /// <summary>
    /// Capacity of internal array.
    /// </summary>
    public int MaxLength { get; set; } = 0;

    /// <summary>
    /// Add property name considering depth.
    /// </summary>
    internal void AddPropertyName(string? name)
    {
        Depth++;
        MaxLength = Math.Max(MaxLength, Depth + 1);

        if (_path.Length < Depth)
        {
            var newPath = ArrayPool<string?>.Shared.Rent(MaxLength);
            Array.Copy(_path, newPath, _path.Length);
            ArrayPool<string?>.Shared.Return(_path);
            _path = newPath;
        }

        _path[Depth] = name;
    }

    internal void RemovePropertyName()
    {
        if (Depth > -1)
        {
            Depth--;
        }
    }

    /// <summary>
    /// Get property name by depth level.
    /// </summary>
    public string? GetPropertyName(int index) 
        => index > Depth ? null : _path[index];

    /// <summary>
    /// Get property name indexed from the end of path.
    /// </summary>
    public string? GetPropertyNameReverse(int reversedIndex)
    {
        var index = Depth - reversedIndex;
        return index < 0 ? null : _path[index];
    }

    public override string ToString() => string.Join('.', _path, 0, Depth + 1);
}