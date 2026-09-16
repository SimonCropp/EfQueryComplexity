public class ValueTests
{
    [Test]
    public async Task LiteralTake()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxTake = 10});

        var exception = Assert.Throws<QueryComplexityException>(() => context.Employees.Take(5000).ToQueryString());

        var violation = exception.Violations.Single();
        await Assert.That(violation.Limit).IsEqualTo("MaxTake");
        await Assert.That(violation.Actual).IsEqualTo(5000);
    }

    [Test]
    public async Task TakeAtLevel()
    {
        var (context, logs) = ContextBuilder.Build(logAt: Limits.None with {MaxTake = 10});

        context.Employees.Take(10).ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(0);
    }

    // The same shape is compiled once, so this only passes if the values are read for each execution
    [Test]
    public void CheckedForEveryExecution()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxTake = 10});

        var small = 5;
        context.Employees.Take(small).ToQueryString();

        var large = 5000;
        Assert.Throws<QueryComplexityException>(() => context.Employees.Take(large).ToQueryString());
    }

    [Test]
    public async Task TakeInsideSubquery()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxTake = 10});

        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Departments.Select(_ => _.Employees.Take(5000).Count()).ToQueryString());

        await Assert.That(exception.Violations.Single().Actual).IsEqualTo(5000);
    }

    [Test]
    public async Task ContainsList()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxInValues = 10});
        var ids = Enumerable.Range(0, 50).ToList();

        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Employees.Where(_ => ids.Contains(_.Id)).ToQueryString());

        var violation = exception.Violations.Single();
        await Assert.That(violation.Limit).IsEqualTo("MaxInValues");
        await Assert.That(violation.Actual).IsEqualTo(50);
    }

    [Test]
    public async Task ContainsArray()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxInValues = 10});
        var ids = Enumerable.Range(0, 50).ToArray();

        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Employees.Where(_ => ids.Contains(_.Id)).ToQueryString());

        await Assert.That(exception.Violations.Single().Actual).IsEqualTo(50);
    }

    [Test]
    public async Task ContainsHashSet()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxInValues = 10});
        var ids = Enumerable.Range(0, 50).ToHashSet();

        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Employees.Where(_ => ids.Contains(_.Id)).ToQueryString());

        await Assert.That(exception.Violations.Single().Actual).IsEqualTo(50);
    }

    [Test]
    public async Task ContainsInlineValues()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxInValues = 2});

        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Employees.Where(_ => new[] {1, 2, 3}.Contains(_.Id)).ToQueryString());

        await Assert.That(exception.Violations.Single().Actual).IsEqualTo(3);
    }

    [Test]
    public async Task ContainsSmallList()
    {
        var (context, logs) = ContextBuilder.Build(logAt: Limits.None with {MaxInValues = 10});
        var ids = Enumerable.Range(0, 10).ToList();

        context.Employees.Where(_ => ids.Contains(_.Id)).ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ValueLogsOncePerCompiledQuery()
    {
        var (context, logs) = ContextBuilder.Build(logAt: Limits.None with {MaxTake = 10});
        var take = 5000;

        context.Employees.Take(take).ToQueryString();
        context.Employees.Take(take).ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(1);
    }

    [Test]
    public void CompiledQuery()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxTake = 10});
        var query = EF.CompileAsyncQuery((TestDbContext data, int take) => data.Employees.Take(take));

        // Lazy, so nothing reaches a database
        _ = query(context, 5);

        Assert.Throws<QueryComplexityException>(() => query(context, 5000));
    }

    [Test]
    public async Task QueryCompilerOnlyReplacedForValueLevels()
    {
        await Assert.That(ReplacesQueryCompiler(Limits.None)).IsFalse();
        await Assert.That(ReplacesQueryCompiler(Limits.None with {MaxTake = 10})).IsTrue();
        await Assert.That(ReplacesQueryCompiler(Limits.None with {MaxInValues = 10})).IsTrue();
    }

    static bool ReplacesQueryCompiler(QueryComplexityLimits logAt)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer("Server=.;Database=Test;")
            .UseQueryComplexity(logAt)
            .Options;

        var replaced = options.FindExtension<CoreOptionsExtension>()?.ReplacedServices;
        return replaced?.Values.Contains(typeof(ComplexityQueryCompiler)) == true;
    }
}
