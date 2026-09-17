/// <summary>
/// Counts the values of a Contains list, which arrives as a parameter value.
/// </summary>
static class Counter
{
    static ConcurrentDictionary<Type, Func<object, int>?> counts = new();

    static MethodInfo countOf = typeof(Counter).GetMethod(nameof(CountOf), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static int Count(object? value)
    {
        switch (value)
        {
            case null or string:
                return 0;
            case ICollection collection:
                return collection.Count;
        }

        // A HashSet, a LINQ iterator, and anything else that only implements the generic interfaces
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

    // Built once for each type, since the element type is only known at runtime, and finding it
    // would otherwise be paid for every execution of every query that uses one
    static Func<object, int>? BuildCount(Type type)
    {
        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface.IsGenericType &&
                @interface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return countOf
                    .MakeGenericMethod(@interface.GetGenericArguments()[0])
                    .CreateDelegate<Func<object, int>>();
            }
        }

        return null;
    }

    static int CountOf<T>(object value)
    {
        var values = (IEnumerable<T>) value;

        // Also answered by a LINQ iterator that knows its count, such as Select over a list, so the
        // selector is not run just to count
        if (values.TryGetNonEnumeratedCount(out var count))
        {
            return count;
        }

        if (values is IReadOnlyCollection<T> collection)
        {
            return collection.Count;
        }

        // Enumerated as T, so a value type is not boxed
        var enumerated = 0;

        foreach (var unused in values)
        {
            enumerated++;
        }

        return enumerated;
    }

    static int Enumerate(IEnumerable enumerable)
    {
        var count = 0;

        foreach (var unused in enumerable)
        {
            count++;
        }

        return count;
    }
}
