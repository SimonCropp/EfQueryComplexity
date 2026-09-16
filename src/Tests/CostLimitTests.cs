[ParallelLimiter<DatabaseParallelLimit>]
public class CostLimitTests
{
    [Test]
    public async Task CheapQueryRuns()
    {
        await using var database = await AssemblySetup.SqlInstance.Build();
        await using var context = Build(database);

        await Assert.That(await context.Companies.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task ExpensiveQueryIsRefused()
    {
        await using var database = await AssemblySetup.SqlInstance.Build();
        await using var context = Build(database);

        // Runs first so the connection has been opened, closed and returned to the pool. Reusing a
        // pooled connection resets session state, so the limit has to be applied again.
        await context.Companies.CountAsync();

        var exception = await Assert.ThrowsAsync<SqlException>(
            () => context.Database
                .SqlQueryRaw<long>(
                    """
                    select count_big(*) as Value
                    from sys.all_columns a
                      cross join sys.all_columns b
                      cross join sys.all_columns c
                    """)
                .SingleAsync());

        // The query governor refused it before it ran
        await Assert.That(exception!.Number).IsEqualTo(8649);
    }

    static TestDbContext Build(SqlDatabase<TestDbContext> database) =>
        new(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(database.ConnectionString)
                .UseQueryComplexity(Limits.None, sqlServerCostLimit: 1)
                .Options);
}
