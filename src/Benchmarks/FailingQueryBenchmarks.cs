// A query over a throw level, executed repeatedly, as when a client keeps sending the same over
// complex query. No database is needed, since the query throws before it would connect.
[MemoryDiagnoser]
public class FailingQueryBenchmarks
{
    BenchmarkContext context = null!;

    [GlobalSetup]
    public void Setup()
    {
        // The same levels for both, since a throw level below its log level is rejected
        var levels = QueryComplexityLimits.LogDefaults with {MaxOperators = 2};
        context = new(
            new DbContextOptionsBuilder<BenchmarkContext>()
                .UseSqlServer("Server=.;Database=Benchmarks;")
                .UseQueryComplexity(levels, levels)
                .Options);
    }

    [GlobalCleanup]
    public void Cleanup() =>
        context.Dispose();

    [Benchmark]
    public int Execute()
    {
        try
        {
            context.Customers
                .Where(customer => customer.Orders.Any(_ => _.Quantity > 5))
                .OrderBy(_ => _.Name)
                .Select(_ => _.Orders.Count)
                .Take(10)
                .ToQueryString();
        }
        catch (QueryComplexityException exception)
        {
            return exception.Violations.Count;
        }

        throw new UnreachableException("The query is expected to exceed MaxOperators.");
    }
}
