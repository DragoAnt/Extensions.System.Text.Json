namespace DragoAnt.System.Text.Json.Observer;

internal static class JsonTokenTypeExtensions
{
    /// <summary>
    /// Checks if <see cref="JsonTokenType"/> is value type.
    /// </summary>
    public static bool IsValueToken(this JsonTokenType type)
    {
        return type switch
        {
            JsonTokenType.String
                or JsonTokenType.Number
                or JsonTokenType.True
                or JsonTokenType.False
                or JsonTokenType.Null
                => true,

            JsonTokenType.None
                or JsonTokenType.StartObject
                or JsonTokenType.EndObject
                or JsonTokenType.StartArray
                or JsonTokenType.EndArray
                or JsonTokenType.PropertyName
                or JsonTokenType.Comment
                => false,

            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
