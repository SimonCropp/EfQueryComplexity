/// <summary>
/// Which operators return some of their source's rows, each at most once and unchanged.
/// </summary>
static class RowOperators
{
    public static bool KeepsRows(MethodInfo method)
    {
        var declaringType = method.DeclaringType;
        var name = method.Name;

        if (declaringType == typeof(Queryable) ||
            declaringType == typeof(Enumerable))
        {
            return name is
                nameof(Queryable.Where) or
                nameof(Queryable.OrderBy) or
                nameof(Queryable.OrderByDescending) or
                nameof(Queryable.ThenBy) or
                nameof(Queryable.ThenByDescending) or
                nameof(Queryable.Skip) or
                nameof(Queryable.Take) or
                nameof(Queryable.Distinct) or
                nameof(Queryable.Reverse) or
                nameof(Queryable.OfType) or
                nameof(Queryable.Cast);
        }

        // These change how the rows are loaded, not which rows are returned
        if (declaringType == typeof(EntityFrameworkQueryableExtensions))
        {
            return name is
                nameof(EntityFrameworkQueryableExtensions.Include) or
                nameof(EntityFrameworkQueryableExtensions.ThenInclude) or
                nameof(EntityFrameworkQueryableExtensions.AsNoTracking) or
                nameof(EntityFrameworkQueryableExtensions.AsNoTrackingWithIdentityResolution) or
                nameof(EntityFrameworkQueryableExtensions.AsTracking) or
                nameof(EntityFrameworkQueryableExtensions.IgnoreAutoIncludes) or
                nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters) or
                nameof(EntityFrameworkQueryableExtensions.TagWith) or
                nameof(EntityFrameworkQueryableExtensions.TagWithCallSite);
        }

        return declaringType == typeof(RelationalQueryableExtensions) &&
               name is
                   nameof(RelationalQueryableExtensions.AsSplitQuery) or
                   nameof(RelationalQueryableExtensions.AsSingleQuery);
    }
}
