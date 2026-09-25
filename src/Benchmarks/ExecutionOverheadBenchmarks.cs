// The cost added to each request once its query is compiled and cached, without the noise of a
// database round trip. ToQueryString runs the cached delegate, including the value checks, without
// connecting. Only the difference between configurations is meaningful, since ToQueryString does
// work an execution does not, and leaves out the round trip that an execution does.
// A fixed count, so every configuration's allocation is measured over the same number of
// operations. Otherwise a one off allocation is spread over a different count for each, which
// moves the per operation figure by more than the difference being measured.
[MemoryDiagnoser]
[InvocationCount(4096)]
public class ExecutionOverheadBenchmarks
{
    DbContextOptions<BenchmarkContext> options = null!;
    List<int> ids = Enumerable.Range(1, 100).ToList();

    [ParamsAllValues]
    public Configuration Configuration { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        options = Configuration.BuildOptions("Server=.;Database=Benchmarks;");
        // Compile once, so every measured call hits the compiled query cache
        Execute();
    }

    // A new context for each call, as each request in an application has
    [Benchmark]
    public string Execute()
    {
        using var context = new BenchmarkContext(options);
        return context.CustomersIn(ids).ToQueryString();
    }
}
