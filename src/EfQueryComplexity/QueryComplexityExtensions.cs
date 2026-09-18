namespace EfQueryComplexity;

/// <summary>
/// Enables and controls query complexity checks.
/// </summary>
public static class QueryComplexityExtensions
{
    // A singleton interceptor is part of the key for the internal service provider, so a new
    // instance for every options build would also build a new provider
    static QueryInterceptor queryInterceptor = new();
    static CostLimitInterceptor costLimitInterceptor = new();

    /// <summary>
    /// Enables query complexity checks.
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="logAt">
    /// The levels at which a query is logged as QueryComplexityEventId.LimitExceeded. Defaults to
    /// <see cref="QueryComplexityLimits.LogDefaults" />.
    /// </param>
    /// <param name="throwAt">
    /// The levels at which a query throws <see cref="QueryComplexityException" />. Null, the
    /// default, means no query throws.
    /// </param>
    /// <param name="sqlServerCostLimit">
    /// When set, SQL Server refuses any statement whose estimated plan cost is greater than this
    /// value, with error 8649. Must be greater than zero, since SQL Server treats zero as the query
    /// governor being off. SQL Server only.
    /// </param>
    public static DbContextOptionsBuilder UseQueryComplexity(
        this DbContextOptionsBuilder builder,
        QueryComplexityLimits? logAt = null,
        QueryComplexityLimits? throwAt = null,
        int? sqlServerCostLimit = null)
    {
        // SQL Server reads a limit of zero as the query governor being off, which is the opposite of
        // what passing zero looks like it asks for
        if (sqlServerCostLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sqlServerCostLimit),
                sqlServerCostLimit,
                "Must be greater than zero. SQL Server treats a cost limit of zero as the query governor being off.");
        }

        var extension = new QueryComplexityOptionsExtension(
            logAt ?? QueryComplexityLimits.LogDefaults,
            throwAt,
            sqlServerCostLimit);

        var core = builder.Options.FindExtension<CoreOptionsExtension>();

        if (core?.SingletonInterceptors?.Contains(queryInterceptor) != true)
        {
            builder.AddInterceptors(queryInterceptor);
        }

        if (sqlServerCostLimit != null &&
            core?.Interceptors?.Contains(costLimitInterceptor) != true)
        {
            builder.AddInterceptors(costLimitInterceptor);
        }

        // Entity Framework keys a compiled query on the model it was compiled for, so the levels have
        // to be part of the key for the model cache. Without that, a context sharing an IMemoryCache
        // with one configured with other levels is handed queries that were checked under those.
        var factory = ComplexityModelCacheKeyFactory.Replacement(core);
        if (factory != null &&
            factory != typeof(ComplexityModelCacheKeyFactory))
        {
            throw new InvalidOperationException(
                $"UseQueryComplexity has to replace IModelCacheKeyFactory, so that contexts with different levels never share a model, and so never share a compiled query that was checked under other levels. It is already replaced with {factory}, and both replacements cannot be in effect. Remove the other one.");
        }

        builder.ReplaceService<IModelCacheKeyFactory, ComplexityModelCacheKeyFactory>();

        // Take and Contains values only exist while a query executes, so they are checked by a query
        // compiler rather than by the interceptor. The same compiler caches a query that throws, so it
        // is also registered whenever a query can throw.
        if (extension.HasValueLevels ||
            throwAt != null)
        {
            ComplexityQueryCompiler.Register(builder);
        }

        ((IDbContextOptionsBuilderInfrastructure) builder).AddOrUpdateExtension(extension);

        return builder;
    }

    /// <summary>
    /// Enables query complexity checks.
    /// </summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="logAt">
    /// The levels at which a query is logged as QueryComplexityEventId.LimitExceeded. Defaults to
    /// <see cref="QueryComplexityLimits.LogDefaults" />.
    /// </param>
    /// <param name="throwAt">
    /// The levels at which a query throws <see cref="QueryComplexityException" />. Null, the
    /// default, means no query throws.
    /// </param>
    /// <param name="sqlServerCostLimit">
    /// When set, SQL Server refuses any statement whose estimated plan cost is greater than this
    /// value, with error 8649. Must be greater than zero, since SQL Server treats zero as the query
    /// governor being off. SQL Server only.
    /// </param>
    public static DbContextOptionsBuilder<TContext> UseQueryComplexity<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        QueryComplexityLimits? logAt = null,
        QueryComplexityLimits? throwAt = null,
        int? sqlServerCostLimit = null)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder) builder).UseQueryComplexity(logAt, throwAt, sqlServerCostLimit);
        return builder;
    }

    /// <summary>
    /// Skips every complexity check for this query.
    /// </summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to skip checks for.</param>
    public static IQueryable<T> IgnoreQueryComplexity<T>(this IQueryable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Provider.CreateQuery<T>(
            Expression.Call(
                Markers.IgnoreMethod.MakeGenericMethod(typeof(T)),
                source.Expression));
    }

    /// <summary>
    /// Replaces complexity levels for this query.
    /// </summary>
    /// <typeparam name="T">The element type of the query.</typeparam>
    /// <param name="source">The query to change levels for.</param>
    /// <param name="limits">The levels to replace. Every level left null keeps the configured value.</param>
    public static IQueryable<T> WithQueryComplexity<T>(this IQueryable<T> source, QueryComplexityOverride limits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);

        return source.Provider.CreateQuery<T>(
            Expression.Call(
                Markers.OverrideMethod.MakeGenericMethod(typeof(T)),
                source.Expression,
                Expression.Constant(limits)));
    }
}
