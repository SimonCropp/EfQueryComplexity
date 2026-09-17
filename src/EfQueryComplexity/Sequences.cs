/// <summary>
/// Reads what a sequence type holds.
/// </summary>
static class Sequences
{
    // A string is enumerable, but it is a value, not a sequence of rows
    public static bool IsSequence(Type type) =>
        type != typeof(string) &&
        typeof(IEnumerable).IsAssignableFrom(type);

    // What a sequence holds, or the type itself when it is not a sequence or has no element type
    public static Type ElementType(Type type)
    {
        if (!IsSequence(type))
        {
            return type;
        }

        // GetInterfaces does not return the type itself, so a type that is IEnumerable<T> is checked
        // first
        if (IsGenericEnumerable(type))
        {
            return type.GetGenericArguments()[0];
        }

        foreach (var @interface in type.GetInterfaces())
        {
            if (IsGenericEnumerable(@interface))
            {
                return @interface.GetGenericArguments()[0];
            }
        }

        return type;
    }

    static bool IsGenericEnumerable(Type type) =>
        type.IsGenericType &&
        type.GetGenericTypeDefinition() == typeof(IEnumerable<>);
}
