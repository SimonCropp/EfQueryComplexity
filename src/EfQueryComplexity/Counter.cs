/// <summary>
/// Counts the values of a Contains list, which arrives as a parameter value.
/// </summary>
static class Counter
{
    static ConcurrentDictionary<Type, Func<object, int>?> counts = new();

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
        var count = counts.GetOrAdd(value.GetType(), BuildCount);
        if (count != null)
        {
            return count(value);
        }

        if (value is IEnumerable enumerable)
        {
            return Enumerate(enumerable);
        }

        return 0;
    }

    // Compiled once for each type, since reading the property through reflection would be paid for
    // every execution of every query that uses one
    static Func<object, int>? BuildCount(Type type)
    {
        foreach (var @interface in type.GetInterfaces())
        {
            if (!@interface.IsGenericType)
            {
                continue;
            }

            var definition = @interface.GetGenericTypeDefinition();
            if (definition != typeof(ICollection<>) &&
                definition != typeof(IReadOnlyCollection<>))
            {
                continue;
            }

            var property = @interface.GetProperty("Count");
            if (property == null)
            {
                continue;
            }

            var parameter = Expression.Parameter(typeof(object));
            var lambda = Expression.Lambda<Func<object, int>>(
                Expression.Property(
                    Expression.Convert(parameter, @interface),
                    property),
                parameter);
            return lambda.Compile();
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
