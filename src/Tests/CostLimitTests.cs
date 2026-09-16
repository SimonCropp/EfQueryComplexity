using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

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

    // A synchronous open goes through a different interceptor method than an async one
    [Test]
    public async Task ExpensiveQueryIsRefusedSynchronously()
    {
        await using var database = await AssemblySetup.SqlInstance.Build();
        await using var context = Build(database);

        context.Companies.Count();

        var exception = Assert.Throws<SqlException>(
            () => context.Database
                .SqlQueryRaw<long>(
                    """
                    select count_big(*) as Value
                    from sys.all_columns a
                      cross join sys.all_columns b
                      cross join sys.all_columns c
                    """)
                .Single());

        await Assert.That(exception.Number).IsEqualTo(8649);
    }

    [Test]
    public async Task OtherConnectionTypeThrows()
    {
        await using var context = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(new FakeConnection())
                .UseQueryComplexity(Limits.None, sqlServerCostLimit: 1)
                .Options);

        var exception = Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());

        await Assert.That(exception.Message)
            .IsEqualTo($"sqlServerCostLimit is SQL Server only, but the connection is {typeof(FakeConnection).FullName}.");
    }

    [Test]
    public async Task OtherConnectionTypeThrowsAsync()
    {
        await using var context = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(new FakeConnection())
                .UseQueryComplexity(Limits.None, sqlServerCostLimit: 1)
                .Options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.OpenConnectionAsync());
    }

    [Test]
    public async Task InterceptorWithoutLimitDoesNothing()
    {
        await using var context = BuildWithoutLimit();

        var database = context.Database;
        await database.OpenConnectionAsync();
        await database.CloseConnectionAsync();
        await database.OpenConnectionAsync();
    }

    // A synchronous open goes through a different interceptor method than an async one. Not async, so
    // the open stays synchronous.
    [Test]
    public void InterceptorWithoutLimitDoesNothingSynchronously()
    {
        using var context = BuildWithoutLimit();

        context.Database.OpenConnection();
    }

    static TestDbContext Build(SqlDatabase<TestDbContext> database) =>
        new(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(database.ConnectionString)
                .UseQueryComplexity(Limits.None, sqlServerCostLimit: 1)
                .Options);

    // Calling UseQueryComplexity again without a cost limit leaves the interceptor registered, and it
    // then has nothing to apply
    static TestDbContext BuildWithoutLimit() =>
        new(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(new FakeConnection())
                .UseQueryComplexity(Limits.None, sqlServerCostLimit: 1)
                .UseQueryComplexity(Limits.None)
                .Options);

    // Opens without connecting to anything, so the interceptor sees a connection that is not
    // SqlConnection
    class FakeConnection :
        DbConnection
    {
        ConnectionState state = ConnectionState.Closed;

        [AllowNull]
        public override string ConnectionString { get; set; } = "";

        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override ConnectionState State => state;

        public override void Open() =>
            state = ConnectionState.Open;

        public override void Close() =>
            state = ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) =>
            throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() =>
            throw new NotSupportedException();
    }
}
