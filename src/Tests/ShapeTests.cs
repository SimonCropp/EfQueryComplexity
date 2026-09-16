public class ShapeTests
{
    static QueryComplexityLimits Nodes(QueryComplexityLimits limits, int value) =>
        limits with {MaxNodes = value};

    static QueryComplexityLimits Depth(QueryComplexityLimits limits, int value) =>
        limits with {MaxDepth = value};

    static QueryComplexityLimits Operators(QueryComplexityLimits limits, int value) =>
        limits with {MaxOperators = value};

    static QueryComplexityLimits NavigationDepth(QueryComplexityLimits limits, int value) =>
        limits with {MaxNavigationDepth = value};

    static QueryComplexityLimits Includes(QueryComplexityLimits limits, int value) =>
        limits with {MaxIncludes = value};

    static QueryComplexityLimits IncludeDepth(QueryComplexityLimits limits, int value) =>
        limits with {MaxIncludeDepth = value};

    [Test]
    public Task NodeLevel() =>
        AssertLevel(context => context.Employees.Where(_ => _.Salary > 10), Nodes);

    [Test]
    public Task DepthLevel() =>
        AssertLevel(context => context.Employees.Where(_ => _.Salary > 10), Depth);

    [Test]
    public Task OperatorLevel() =>
        AssertLevel(context => context.Employees.Where(_ => _.Salary > 10), Operators);

    [Test]
    public Task NavigationDepthLevel() =>
        AssertLevel(context => context.EmployeeTasks.Where(_ => _.Employee.Department.Company.Name == "Acme"), NavigationDepth);

    [Test]
    public Task IncludeLevel() =>
        AssertLevel(context => context.Companies.Include(_ => _.Departments), Includes);

    [Test]
    public Task IncludeDepthLevel() =>
        AssertLevel(context => context.Companies.Include(_ => _.Departments).ThenInclude(_ => _.Employees), IncludeDepth);

    [Test]
    public async Task NavigationDepthCountsNavigations() =>
        await Assert.That(
                Measure(
                    context => context.EmployeeTasks.Where(_ => _.Employee.Department.Company.Name == "Acme"),
                    NavigationDepth))
            .IsEqualTo(3);

    [Test]
    public async Task NavigationDepthIgnoresScalars() =>
        await Assert.That(
                Measure(
                    context => context.Employees.Where(_ => _.Department.Name == "Engineering"),
                    NavigationDepth))
            .IsEqualTo(1);

    [Test]
    public async Task IncludeDepthCountsThenIncludes() =>
        await Assert.That(
                Measure(
                    context => context.Companies
                        .Include(_ => _.Departments)
                        .ThenInclude(_ => _.Employees)
                        .ThenInclude(_ => _.Tasks),
                    IncludeDepth))
            .IsEqualTo(3);

    [Test]
    public async Task IncludeDepthCountsStringPaths() =>
        await Assert.That(
                Measure(
                    context => context.Companies.Include("Departments.Employees.Tasks"),
                    IncludeDepth))
            .IsEqualTo(3);

    [Test]
    public async Task FilteredIncludeCountsOneNavigation() =>
        await Assert.That(
                Measure(
                    context => context.Companies.Include(_ => _.Departments.Where(department => department.Name != "")),
                    IncludeDepth))
            .IsEqualTo(1);

    [Test]
    public async Task IncludeCountCountsEachInclude() =>
        await Assert.That(
                Measure(
                    context => context.Companies
                        .Include(_ => _.Departments)
                        .Include("Departments.Employees"),
                    Includes))
            .IsEqualTo(2);

    [Test]
    public Task Message()
    {
        var (context, _) = ContextBuilder.Build(
            throwAt: Limits.None with
            {
                MaxNodes = 1,
                MaxOperators = 1
            });

        return Throws(() => context.Employees.Where(_ => _.Salary > 10).Take(5).ToQueryString())
            .IgnoreStackTrace();
    }

    [Test]
    public async Task ThrowWinsOverLog()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with {MaxNodes = 1},
            throwAt: Limits.None with {MaxNodes = 1});

        Assert.Throws<QueryComplexityException>(() => context.Employees.Where(_ => _.Salary > 10).ToQueryString());

        await Assert.That(logs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task LogsOncePerShape()
    {
        var (context, logs) = ContextBuilder.Build(logAt: Limits.None with {MaxNodes = 1});

        context.Employees.Where(_ => _.Salary > 10).ToQueryString();
        context.Employees.Where(_ => _.Salary > 10).ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(1);
    }

    static async Task AssertLevel(
        Func<TestDbContext, IQueryable> query,
        Func<QueryComplexityLimits, int, QueryComplexityLimits> level)
    {
        var actual = Measure(query, level);

        // At the level nothing fires
        var (atLevel, atLevelLogs) = ContextBuilder.Build(
            logAt: level(Limits.None, actual),
            throwAt: level(Limits.None, actual));
        query(atLevel).ToQueryString();
        await Assert.That(atLevelLogs.Count).IsEqualTo(0);

        // Over the log level it is logged
        var (overLog, logs) = ContextBuilder.Build(logAt: level(Limits.None, actual - 1));
        query(overLog).ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(1);

        // Over the throw level it throws
        var (overThrow, _) = ContextBuilder.Build(throwAt: level(Limits.None, actual - 1));
        var exception = Assert.Throws<QueryComplexityException>(() => query(overThrow).ToQueryString());
        var violation = exception.Violations.Single();
        await Assert.That(violation.Actual).IsEqualTo(actual);
        await Assert.That(violation.Max).IsEqualTo(actual - 1);
    }

    // A level of zero always fires, and the violation reports what the query measures
    static int Measure(
        Func<TestDbContext, IQueryable> query,
        Func<QueryComplexityLimits, int, QueryComplexityLimits> level)
    {
        var (context, _) = ContextBuilder.Build(throwAt: level(Limits.None, 0));
        var exception = Assert.Throws<QueryComplexityException>(() => query(context).ToQueryString());
        return exception.Violations.Single().Actual!.Value;
    }
}
