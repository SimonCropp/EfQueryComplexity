/// <summary>
/// The methods that carry per query configuration through query compilation.
/// </summary>
/// <remarks>
/// A call to one of these is put in the query by the public extension methods, read while the query
/// is compiled, and then removed before Entity Framework translates the query.
/// </remarks>
static class Markers
{
    public static readonly MethodInfo IgnoreMethod = typeof(Markers)
        .GetMethod(nameof(IgnoreQueryComplexity), BindingFlags.Static | BindingFlags.Public)!;

    public static readonly MethodInfo OverrideMethod = typeof(Markers)
        .GetMethod(nameof(WithQueryComplexity), BindingFlags.Static | BindingFlags.Public)!;

    // Never executed. Returning the source keeps it harmless for a provider other than Entity
    // Framework, which may evaluate the call instead of translating it.
    public static IQueryable<T> IgnoreQueryComplexity<T>(IQueryable<T> source) =>
        source;

    // NotParameterized keeps the levels as a constant. Without it Entity Framework turns any top
    // level argument into a parameter, and the values would not be readable while the query is
    // compiled. As a constant it is also part of the compiled query cache key, so a query with
    // different levels is compiled and checked separately.
    public static IQueryable<T> WithQueryComplexity<T>(IQueryable<T> source, [NotParameterized] QueryComplexityOverride limits) =>
        source;

    public static bool IsIgnore(MethodInfo method) =>
        IsMarker(method, IgnoreMethod);

    public static bool IsOverride(MethodInfo method) =>
        IsMarker(method, OverrideMethod);

    static bool IsMarker(MethodInfo method, MethodInfo marker) =>
        method.IsGenericMethod &&
        method.GetGenericMethodDefinition() == marker;
}
