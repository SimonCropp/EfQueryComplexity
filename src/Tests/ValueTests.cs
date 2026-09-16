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

    // Neither a collection nor a generic collection, so the values are counted by enumerating them
    [Test]
    public async Task ContainsEnumerable()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxInValues = 10});
        var ids = Ids(50);

        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Employees.Where(_ => ids.Contains(_.Id)).ToQueryString());

        await Assert.That(exception.Violations.Single().Actual).IsEqualTo(50);
    }

    // Select over a list knows its count, so the check does not run the selector
    [Test]
    public async Task ContainsSelect()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxInValues = 10});
        var selected = 0;
        var ids = Enumerable.Range(0, 50)
            .ToList()
            .Select(_ =>
            {
                selected++;
                return _;
            });

        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Employees.Where(_ => ids.Contains(_.Id)).ToQueryString());

        await Assert.That(exception.Violations.Single().Actual).IsEqualTo(50);
        await Assert.That(selected).IsEqualTo(0);
    }

    static IEnumerable<int> Ids(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return index;
        }
    }

    [Test]
    public async Task CompiledQueryContainsArray()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxInValues = 10});
        var query = EF.CompileQuery((TestDbContext data, int[] ids) => data.Employees.Where(_ => ids.Contains(_.Id)));

        var exception = Assert.Throws<QueryComplexityException>(() => query(context, Enumerable.Range(0, 50).ToArray()));

        await Assert.That(exception.Violations.Single().Actual).IsEqualTo(50);
    }

    [Test]
    public async Task CompiledQueryContainsInlineValues()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxInValues = 2});
        var query = EF.CompileQuery((TestDbContext data) => data.Employees.Where(_ => new[] {1, 2, 3}.Contains(_.Id)));

        var exception = Assert.Throws<QueryComplexityException>(() => query(context));

        await Assert.That(exception.Violations.Single().Actual).IsEqualTo(3);
    }

    // A value level is set, but the query has nothing for it to check
    [Test]
    public async Task QueryWithoutValues()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with {MaxTake = 1},
            throwAt: Limits.None with {MaxInValues = 1});

        context.Employees.Where(_ => _.Salary > 10).ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(0);
    }

    // Calling UseQueryComplexity again without value levels leaves the query compiler replaced, and it
    // then has nothing to check
    [Test]
    public async Task QueryCompilerWithoutValueLevels()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with {MaxTake = 1},
            configure: _ => _.UseQueryComplexity(Limits.None));

        var replaced = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()!
            .ReplacedServices!;
        await Assert.That(replaced.Values.Contains(typeof(ComplexityQueryCompiler))).IsTrue();

        context.Employees.Take(5000).ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(0);
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

    // Replaced for a value level, or for throw levels of any kind, since the compiler also caches a
    // query that throws
    [Test]
    public async Task QueryCompilerOnlyReplacedWhenNeeded()
    {
        await Assert.That(ReplacesQueryCompiler(Limits.None)).IsFalse();
        await Assert.That(ReplacesQueryCompiler(Limits.None with {MaxNodes = 10})).IsFalse();
        await Assert.That(ReplacesQueryCompiler(Limits.None with {MaxTake = 10})).IsTrue();
        await Assert.That(ReplacesQueryCompiler(Limits.None with {MaxInValues = 10})).IsTrue();
        await Assert.That(ReplacesQueryCompiler(Limits.None, Limits.None)).IsTrue();
    }

    static bool ReplacesQueryCompiler(QueryComplexityLimits logAt, QueryComplexityLimits? throwAt = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer("Server=.;Database=Test;")
            .UseQueryComplexity(logAt, throwAt)
            .Options;

        var replaced = options.FindExtension<CoreOptionsExtension>()?.ReplacedServices;
        return replaced?.Values.Contains(typeof(ComplexityQueryCompiler)) == true;
    }
}
