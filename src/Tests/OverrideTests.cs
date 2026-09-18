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

    // An override of true checks every type, whichever types the configured levels name
    [Test]
    public void UnboundedOverrideChecksEveryType()
    {
        var (context, _) = ContextBuilder.Build(
            throwAt: Limits.None with
            {
                RejectUnbounded = UnboundedEntities.Only(typeof(Department))
            });

        Assert.Throws<QueryComplexityException>(
            () => context.Employees
                .WithQueryComplexity(
                    new()
                    {
                        RejectUnbounded = true
                    })
                .ToQueryString());
    }

    [Test]
    public void UnboundedOverrideChecksNoType()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {RejectUnbounded = true});

        context.Employees
            .WithQueryComplexity(
                new()
                {
                    RejectUnbounded = false
                })
            .ToQueryString();
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

    // The value checks are only registered when the configured levels have a value level, so an
    // override cannot turn them on for one query, and silently skipping the check would be worse
    [Test]
    public async Task ValueOverrideNeedsConfiguredValueLevels()
    {
        var (context, _) = ContextBuilder.Build(logAt: Limits.None);

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Employees
                .WithQueryComplexity(
                    new()
                    {
                        MaxTake = 10
                    })
                .Take(5000)
                .ToQueryString());

        await Assert.That(exception.Message).Contains("MaxTake");
    }

    // A shape level has no such restriction, since the interceptor is always registered
    [Test]
    public async Task ShapeOverrideWithoutConfiguredShapeLevels()
    {
        var (context, logs) = ContextBuilder.Build(logAt: Limits.None);

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

    // A query is measured after its markers are removed, so the message prints what was measured
    // rather than a query with extra nodes in it
    [Test]
    public Task MessageExcludesMarkers()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxNodes = 1});

        return Throws(
                () => context.Employees
                    .Where(_ => _.Salary > 10)
                    .WithQueryComplexity(
                        new()
                        {
                            MaxNodes = 2
                        })
                    .ToQueryString())
            .IgnoreStackTrace();
    }

    // The value checks read the markers rather than removing them, so the query is only stripped
    // once a message is built
    [Test]
    public Task ValueMessageExcludesMarkers()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxTake = 10});

        return Throws(
                () => context.Employees
                    .WithQueryComplexity(
                        new()
                        {
                            MaxTake = 20
                        })
                    .Take(5000)
                    .ToQueryString())
            .IgnoreStackTrace();
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

    // The levels are for the whole query, so a marker on a subquery would change the levels of the
    // query containing it, and every query composed over that subquery would skip its checks
    [Test]
    public async Task IgnoreOnASubqueryIsRejected()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {RejectUnbounded = true});
        var lookup = context.Companies.IgnoreQueryComplexity();

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Employees
                .Where(employee => lookup.Any(_ => _.Id == employee.DepartmentId))
                .ToQueryString());

        await Assert.That(exception.Message).Contains("IgnoreQueryComplexity()");
    }

    [Test]
    public async Task OverrideOnASubqueryIsRejected()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {RejectUnbounded = true});
        var lookup = context.Companies
            .WithQueryComplexity(
                new()
                {
                    RejectUnbounded = false
                });

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Employees
                .Where(employee => lookup.Any(_ => _.Id == employee.DepartmentId))
                .ToQueryString());

        await Assert.That(exception.Message).Contains("WithQueryComplexity()");
    }

    // The levels are read while the query is compiled, and a compiled query parameter only has a
    // value once the query runs, so it cannot be honored
    [Test]
    public async Task OverrideFromACompiledQueryParameterIsRejected()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxNodes = 10_000});
        var query = EF.CompileQuery(
            (TestDbContext data, QueryComplexityOverride limits) => data.Employees.WithQueryComplexity(limits));

        var exception = Assert.Throws<InvalidOperationException>(
            () => query(
                context,
                new()
                {
                    MaxNodes = 1
                }));

        await Assert.That(exception.Message).Contains("WithQueryComplexity()");
    }

    // Entity Framework keeps the levels a constant, so the marker is built by hand here
    [Test]
    public void OverrideThatIsNotAConstantIsRejected()
    {
        var (context, _) = ContextBuilder.Build();
        var call = Expression.Call(
            Markers.OverrideMethod.MakeGenericMethod(typeof(Employee)),
            context.Employees.AsQueryable().Expression,
            Expression.Parameter(typeof(QueryComplexityOverride)));

        Assert.Throws<InvalidOperationException>(() => MarkerReader.Strip(call));
    }

    [Test]
    public void NullOverrideThrows()
    {
        var (context, _) = ContextBuilder.Build();

        Assert.Throws<ArgumentNullException>(() => context.Employees.WithQueryComplexity(null!));
    }

    // A subquery does not stop a marker on the query being executed from being read
    [Test]
    public void MarkerOnAQueryThatHasASubquery()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxNodes = 1});

        context.Departments
            .Where(department => context.Employees.Any(_ => _.DepartmentId == department.Id))
            .IgnoreQueryComplexity()
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
