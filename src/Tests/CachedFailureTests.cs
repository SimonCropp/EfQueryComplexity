// Entity Framework does not cache a query that fails to compile, so the failure is cached in its
// place. A query that keeps throwing is then not compiled, measured and printed again.
public class CachedFailureTests
{
    [Test]
    public async Task ThrowsEveryTimeButCompilesOnce()
    {
        var compilations = new CompilationCounter();
        await using var context = Build(compilations);

        var first = Assert.Throws<QueryComplexityException>(() => context.Employees.Where(_ => _.Salary > 10).ToQueryString());
        var second = Assert.Throws<QueryComplexityException>(() => context.Employees.Where(_ => _.Salary > 10).ToQueryString());

        await Assert.That(compilations.Count).IsEqualTo(1);

        // A new exception for each execution, with the same message and violations
        await Assert.That(second).IsNotSameReferenceAs(first);
        await Assert.That(second.Message).IsEqualTo(first.Message);
        await Assert.That(second.Violations).IsEquivalentTo(first.Violations);
    }

    [Test]
    public async Task Async()
    {
        var compilations = new CompilationCounter();
        await using var context = Build(compilations);

        await Assert.ThrowsAsync<QueryComplexityException>(() => context.Employees.Where(_ => _.Salary > 10).ToListAsync());
        await Assert.ThrowsAsync<QueryComplexityException>(() => context.Employees.Where(_ => _.Salary > 10).ToListAsync());

        await Assert.That(compilations.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CompiledQuery()
    {
        var compilations = new CompilationCounter();
        await using var context = Build(compilations);
        var query = EF.CompileQuery((TestDbContext data) => data.Employees.Where(_ => _.Salary > 10));

        Assert.Throws<QueryComplexityException>(() => query(context));
        Assert.Throws<QueryComplexityException>(() => query(context));

        await Assert.That(compilations.Count).IsEqualTo(1);
    }

    static TestDbContext Build(CompilationCounter compilations) =>
        new(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer("Server=.;Database=Test;")
                .EnableServiceProviderCaching(false)
                // Added before UseQueryComplexity, so it sees each compilation before the check throws
                .AddInterceptors(compilations)
                .UseQueryComplexity(Limits.None, Limits.None with {MaxNodes = 1})
                .Options);

    class CompilationCounter :
        IQueryExpressionInterceptor
    {
        public int Count { get; private set; }

        public Expression QueryCompilationStarting(Expression query, QueryExpressionEventData eventData)
        {
            Count++;
            return query;
        }
    }
}
