/// <summary>
/// Counts the values of a Contains list, which arrives as a parameter value.
/// </summary>
static class Counter
{
    static ConcurrentDictionary<Type, PropertyInfo?> counts = new();

    public static int Count(object? value)
    {
        switch (value)
        {
            case null or string:
                return 0;
            case ICollection collection:
                return collection.Count;
        }

        // A HashSet, and anything else that only implements the generic interfaces, still knows its
        // own count
        var property = counts.GetOrAdd(value.GetType(), FindCount);
        if (property != null)
        {
            return (int) property.GetValue(value)!;
        }

        if (value is IEnumerable enumerable)
        {
            return Enumerate(enumerable);
        }

        return 0;
    }

    static PropertyInfo? FindCount(Type type)
    {
        foreach (var @interface in type.GetInterfaces())
        {
            if (!@interface.IsGenericType)
            {
                continue;
            }

            var definition = @interface.GetGenericTypeDefinition();
            if (definition == typeof(ICollection<>) ||
                definition == typeof(IReadOnlyCollection<>))
            {
                return @interface.GetProperty("Count");
            }
        }

        return null;
    }

    static int Enumerate(IEnumerable enumerable)
    {
        var count = 0;

        foreach (var item in enumerable)
        {
            count++;
        }

        return count;
    }
}
