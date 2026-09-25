public enum Configuration
{
    // UseQueryComplexity not called
    Without,
    // Log levels for shape only, so no value check wraps the compiled query
    ShapeOnly,
    // UseQueryComplexity() with LogDefaults, so Take and the Contains list are checked
    LogDefaults,
    // LogDefaults as both the log and the throw levels
    LogAndThrow
}

static class ConfigurationOptions
{
    public static DbContextOptions<BenchmarkContext> BuildOptions(
        this Configuration configuration,
        string connectionString)
    {
        var builder = new DbContextOptionsBuilder<BenchmarkContext>()
            .UseSqlServer(connectionString);
        switch (configuration)
        {
            case Configuration.Without:
                break;
            case Configuration.ShapeOnly:
                builder.UseQueryComplexity(
                    QueryComplexityLimits.LogDefaults with
                    {
                        MaxTake = null,
                        MaxInValues = null
                    });
                break;
            case Configuration.LogDefaults:
                builder.UseQueryComplexity();
                break;
            case Configuration.LogAndThrow:
                builder.UseQueryComplexity(
                    QueryComplexityLimits.LogDefaults,
                    QueryComplexityLimits.LogDefaults);
                break;
            default:
                throw new UnreachableException();
        }

        return builder.Options;
    }
}
