public class OverrideTests
{
    [Test]
    public void IgnoreSkipsShapeChecks()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxNodes = 1});

        context.Employees.Where(_ => _.Salary > 10).IgnoreQueryComplexity().ToQueryString();
    }

    [Test]
    public void IgnoreSkipsValueChecks()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxTake = 10});

        context.Employees.Take(5000).IgnoreQueryComplexity().ToQueryString();
    }

    [Test]
    public void OverrideRaisesLevel()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxNodes = 1});

        context.Employees
            .Where(_ => _.Salary > 10)
            .WithQueryComplexity(
                new()
                {
                    MaxNodes = 10_000
                })
            .ToQueryString();
    }

    [Test]
    public async Task OverrideLowersLevel()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxNodes = 10_000});

        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Employees
                .Where(_ => _.Salary > 10)
                .WithQueryComplexity(
                    new()
                    {
                        MaxNodes = 1
                    })
                .ToQueryString());

        await Assert.That(exception.Violations.Single().Max).IsEqualTo(1);
    }

    // An override changes the levels, but it never starts throwing for a context that was not given
    // throw levels
    [Test]
    public async Task OverrideDoesNotEnableThrowing()
    {
        var (context, logs) = ContextBuilder.Build(logAt: Limits.None with {MaxNodes = 10_000});

        context.Employees
            .Where(_ => _.Salary > 10)
            .WithQueryComplexity(
                new()
                {
                    MaxNodes = 1
                })
            .ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(1);
    }

    [Test]
    public void OutermostOverrideWins()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxNodes = 1});

        context.Employees
            .Where(_ => _.Salary > 10)
            // The inner level would throw, so passing proves the outer one won
            .WithQueryComplexity(
                new()
                {
                    MaxNodes = 2
                })
            .WithQueryComplexity(
                new()
                {
                    MaxNodes = 10_000
                })
            .ToQueryString();
    }

    [Test]
    public async Task MarkersAreRemovedFromSql()
    {
        var (context, _) = ContextBuilder.Build();

        var withMarkers = context.Employees
            .WithQueryComplexity(
                new()
                {
                    MaxNodes = 10_000
                })
            .IgnoreQueryComplexity()
            .Take(5)
            .ToQueryString();

        var plain = context.Employees.Take(5).ToQueryString();

        await Assert.That(withMarkers).IsEqualTo(plain);
    }

    [Test]
    public void MarkerInSubqueryIsRemoved()
    {
        var (context, _) = ContextBuilder.Build();

        context.Departments
            .Where(department => context.Employees.IgnoreQueryComplexity().Any(_ => _.DepartmentId == department.Id))
            .ToQueryString();
    }

    // Each set of levels is a constant in the query, so it is compiled and checked separately
    [Test]
    public async Task DifferentOverridesAreCheckedSeparately()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxNodes = 1});

        context.Employees
            .Where(_ => _.Salary > 10)
            .WithQueryComplexity(
                new()
                {
                    MaxNodes = 10_000
                })
            .ToQueryString();

        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Employees
                .Where(_ => _.Salary > 10)
                .WithQueryComplexity(
                    new()
                    {
                        MaxNodes = 2
                    })
                .ToQueryString());

        await Assert.That(exception.Violations.Single().Max).IsEqualTo(2);
    }
}
