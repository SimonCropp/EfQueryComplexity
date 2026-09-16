public static class ContextBuilder
{
    /// <summary>
    /// A context that needs no database. ToQueryString compiles a query and runs the cached
    /// delegate, so both shape and value checks fire without a connection.
    /// </summary>
    public static (TestDbContext context, List<string> logs) Build(
        QueryComplexityLimits? logAt = null,
        QueryComplexityLimits? throwAt = null,
        int? sqlServerCostLimit = null,
        bool cacheServiceProvider = false,
        Action<DbContextOptionsBuilder<TestDbContext>>? configure = null)
    {
        var logs = new List<string>();
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer("Server=.;Database=Test;")
            // Each context gets its own compiled query cache, so a test never sees a query another
            // test already compiled
            .EnableServiceProviderCaching(cacheServiceProvider)
            .LogTo(logs.Add, [QueryComplexityEventId.LimitExceeded], LogLevel.Debug, DbContextLoggerOptions.None)
            .UseQueryComplexity(logAt ?? Limits.None, throwAt, sqlServerCostLimit);

        configure?.Invoke(builder);

        return (new(builder.Options), logs);
    }
}
