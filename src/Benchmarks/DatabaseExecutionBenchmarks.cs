// What a request costs, executing a compiled and cached query against LocalDB, including the round
// trip and materializing the rows. Needs LocalDB installed.
// A fixed count, so every configuration's allocation is measured over the same number of
// operations. Otherwise a one off allocation is spread over a different count for each, which
// moves the per operation figure by more than the difference being measured.
[MemoryDiagnoser]
[InvocationCount(1024)]
public class DatabaseExecutionBenchmarks
{
    SqlDatabase<BenchmarkContext> database = null!;
    DbContextOptions<BenchmarkContext> options = null!;
    List<int> ids = Enumerable.Range(1, 100).ToList();

    [ParamsAllValues]
    public Configuration Configuration { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var instance = new SqlInstance<BenchmarkContext>(
            constructInstance: builder => new(builder.Options),
            buildTemplate: async context =>
            {
                await context.Database.EnsureCreatedAsync();
                for (var index = 0; index < 100; index++)
                {
                    context.Customers.Add(
                        new()
                        {
                            Name = $"Customer {index}"
                        });
                }

                await context.SaveChangesAsync();
            });
        database = await instance.Build();
        options = Configuration.BuildOptions(database.ConnectionString);
        // Compile once, so every measured call hits the compiled query cache
        Execute();
    }

    [GlobalCleanup]
    public Task Cleanup() =>
        database.Delete();

    // A new context for each call, as each request in an application has
    [Benchmark]
    public List<Customer> Execute()
    {
        using var context = new BenchmarkContext(options);
        return context.CustomersIn(ids).ToList();
    }
}
