// ReSharper disable All
#pragma warning disable IDE0022
#pragma warning disable IDE0059
namespace Snippets;

public class EnableExample :
    DbContext
{
    #region EnableQueryComplexity

    protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
        builder.UseQueryComplexity();

    #endregion
}

public class LogAtExample :
    DbContext
{
    #region LogAt

    protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
        builder.UseQueryComplexity(
            logAt: QueryComplexityLimits.LogDefaults with
            {
                MaxTake = 500,
                RejectUnbounded = false
            });

    #endregion
}

public class ThrowAtExample :
    DbContext
{
    #region ThrowAt

    protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
        builder.UseQueryComplexity(
            throwAt: new(
                MaxNodes: 4096,
                MaxDepth: 64,
                MaxOperators: 64,
                MaxNavigationDepth: 4,
                MaxIncludes: 16,
                MaxIncludeDepth: 4,
                MaxTake: 1000,
                MaxInValues: 1000,
                RejectUnbounded: true));

    #endregion
}

public class CostLimitExample :
    DbContext
{
    #region SqlServerCostLimit

    protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
        builder
            .UseSqlServer("connection-string")
            .UseQueryComplexity(sqlServerCostLimit: 300);

    #endregion
}

public class ConfigureWarningsExample :
    DbContext
{
    #region EscalateWithConfigureWarnings

    protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
        builder
            .UseQueryComplexity()
            .ConfigureWarnings(
                _ => _.Throw(QueryComplexityEventId.LimitExceeded));

    #endregion
}

public class SnippetExamples
{
    static async Task Ignore()
    {
        AppDbContext context = null!;

        #region IgnoreQueryComplexity

        // Skips every check for this query
        var employees = await context.Employees
            .IgnoreQueryComplexity()
            .ToListAsync();

        #endregion
    }

    static async Task Override()
    {
        AppDbContext context = null!;

        #region WithQueryComplexity

        // Replaces levels for this query only
        var employees = await context.Employees
            .WithQueryComplexity(
                new()
                {
                    MaxTake = 5000
                })
            .Take(5000)
            .ToListAsync();

        #endregion
    }
}

public class SnippetEmployee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class AppDbContext :
    DbContext
{
    public DbSet<SnippetEmployee> Employees => Set<SnippetEmployee>();
}
