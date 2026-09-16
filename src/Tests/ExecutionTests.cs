[ParallelLimiter<DatabaseParallelLimit>]
public class ExecutionTests
{
    [Test]
    public async Task BoundedQueryRuns()
    {
        await using var database = await AssemblySetup.SqlInstance.Build();
        await using var context = database.NewDbContext();

        Recording.Start();
        var employees = await context.Employees
            .OrderBy(_ => _.Name)
            .Take(2)
            .ToListAsync();

        await Verify(employees);
    }

    [Test]
    public async Task ValueViolationThrowsBeforeAnySql()
    {
        await using var database = await AssemblySetup.SqlInstance.Build();
        await using var context = Build(database, Limits.None with {MaxTake = 10});

        Recording.Start();
        await Assert.ThrowsAsync<QueryComplexityException>(() => context.Employees.Take(5000).ToListAsync());

        await Assert.That(Recording.Stop().Count).IsEqualTo(0);
    }

    [Test]
    public async Task IgnoredQueryRuns()
    {
        await using var database = await AssemblySetup.SqlInstance.Build();
        await using var context = Build(
            database,
            Limits.None with
            {
                MaxNodes = 1,
                MaxTake = 10
            });

        var employees = await context.Employees
            .Take(5000)
            .IgnoreQueryComplexity()
            .ToListAsync();

        await Assert.That(employees.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ScalarTerminalIsBounded()
    {
        await using var database = await AssemblySetup.SqlInstance.Build();
        await using var context = Build(database, Limits.None with {RejectUnbounded = true});

        await Assert.That(await context.Employees.CountAsync()).IsEqualTo(2);
    }

    static TestDbContext Build(SqlDatabase<TestDbContext> database, QueryComplexityLimits throwAt) =>
        new(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(database.ConnectionString)
                .UseQueryComplexity(Limits.None, throwAt)
                .Options);
}
