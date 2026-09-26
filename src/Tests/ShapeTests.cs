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

    static QueryComplexityLimits SingleQueryCollections(QueryComplexityLimits limits, int value) =>
        limits with {MaxSingleQueryCollections = value};

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

    // EF.Property names a navigation as a string, and joins like any other
    [Test]
    public async Task NavigationDepthCountsEfProperty() =>
        await Assert.That(
                Measure(
                    context => context.EmployeeTasks.Where(
                        _ => EF.Property<Company>(EF.Property<Department>(_.Employee, "Department"), "Company").Name == "Acme"),
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

    // A cast inside the path is looked through
    [Test]
    public async Task IncludeDepthLooksThroughCasts() =>
        await Assert.That(
                Measure(
                    context => context.Companies
                        // ReSharper disable once RedundantCast
                        .Include(_ => ((Company) (object) _).Departments)
                        .ThenInclude(_ => _.Employees),
                    IncludeDepth))
            .IsEqualTo(2);

    // Not a navigation, and Entity Framework does not evaluate it early, so it is measured before
    // Entity Framework rejects it. The query still has to fail with Entity Framework's error.
    [Test]
    public async Task InvalidIncludeKeepsEntityFrameworkError()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxIncludeDepth = 10});

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Companies.Include(_ => Guid.NewGuid()).ToQueryString());

        await Assert.That(exception.Message).Contains("Include");
    }

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

    [Test]
    public Task SingleQueryCollectionsLevel() =>
        AssertLevel(context => context.Companies.Include(_ => _.Departments).ThenInclude(_ => _.Employees), SingleQueryCollections);

    // Departments is restated to ThenInclude below it, and is one collection
    [Test]
    public async Task SingleQueryCollectionsCountsRestatedIncludeOnce() =>
        await Assert.That(
                Measure(
                    context => context.Companies
                        .Include(_ => _.Departments)
                        .ThenInclude(_ => _.Employees)
                        .Include(_ => _.Departments)
                        .ThenInclude(_ => _.Company),
                    SingleQueryCollections))
            .IsEqualTo(2);

    [Test]
    public async Task SingleQueryCollectionsCountsStringInclude() =>
        await Assert.That(
                Measure(
                    context => context.Companies.Include("Departments.Employees.Tasks"),
                    SingleQueryCollections))
            .IsEqualTo(3);

    [Test]
    public async Task SingleQueryCollectionsCountsProjectedCollections() =>
        await Assert.That(
                Measure(
                    context => context.Companies.Select(
                        _ => new
                        {
                            _.Name,
                            Departments = _.Departments
                                .Select(department => new
                                {
                                    department.Name,
                                    Employees = department.Employees.ToList()
                                })
                                .ToList()
                        }),
                    SingleQueryCollections))
            .IsEqualTo(2);

    // AsSingleQuery overrides a context that splits by default
    [Test]
    public async Task SingleQueryCollectionsCountsAsSingleQuery()
    {
        var (context, _) = ContextBuilder.Build(
            throwAt: SingleQueryCollections(Limits.None, 0),
            configure: SplitByDefault);
        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Companies
                .Include(_ => _.Departments)
                .AsSingleQuery()
                .ToQueryString());
        await Assert.That(exception.Violations.Single().Actual).IsEqualTo(1);
    }

    [Test]
    public Task SingleQueryCollectionsIgnoresReferences() =>
        AssertNoCollections(context => context.Employees.Include(_ => _.Department).ThenInclude(_ => _.Company));

    // A collection only read by an aggregate is a subquery rather than a join
    [Test]
    public Task SingleQueryCollectionsIgnoresAggregates() =>
        AssertNoCollections(
            context => context.Companies.Select(
                _ => new
                {
                    _.Name,
                    Departments = _.Departments.Count(),
                    Staffed = _.Departments.Any(department => department.Employees.Count > 0)
                }));

    [Test]
    public Task SingleQueryCollectionsIgnoresAsSplitQuery() =>
        AssertNoCollections(
            context => context.Companies
                .Include(_ => _.Departments)
                .ThenInclude(_ => _.Employees)
                .AsSplitQuery());

    [Test]
    public Task SingleQueryCollectionsIgnoresSplitByDefault() =>
        AssertNoCollections(
            context => context.Companies
                .Include(_ => _.Departments)
                .ThenInclude(_ => _.Employees),
            SplitByDefault);

    // The query returns names, so there are no companies for the Includes to load into
    [Test]
    public Task SingleQueryCollectionsIgnoresIncludesAProjectionDrops() =>
        AssertNoCollections(
            context => context.Companies
                .Include(_ => _.Departments)
                .ThenInclude(_ => _.Employees)
                .Where(_ => _.Name != "")
                .OrderBy(_ => _.Name)
                .Take(5)
                .Select(_ => _.Name));

    [Test]
    public Task SingleQueryCollectionsIgnoresIncludesADtoDrops() =>
        AssertNoCollections(
            context => context.Companies
                .Include(_ => _.Departments)
                .ThenInclude(_ => _.Employees)
                .Select(
                    _ => new
                    {
                        _.Name,
                        Departments = _.Departments.Count(),
                        Staffed = _.Departments.Any(department => department.Employees.Count > 0)
                    }));

    [Test]
    public async Task SingleQueryCollectionsIgnoresIncludesAnAggregateDrops()
    {
        var (context, _) = ContextBuilder.Build();
        var query = context.Companies
            .Include(_ => _.Departments)
            .ThenInclude(_ => _.Employees);
        var count = Expression.Call(typeof(Queryable), nameof(Queryable.Count), [typeof(Company)], query.Expression);

        await Assert.That(CollectionCounter.Count(count, context.Model, splitByDefault: false)).IsEqualTo(0);
    }

    // The projection loads the department names itself, and the Include is ignored
    [Test]
    public async Task SingleQueryCollectionsCountsProjectionNotDroppedInclude() =>
        await Assert.That(
                Measure(
                    context => context.Companies
                        .Include(_ => _.Departments)
                        .Select(
                            _ => new
                            {
                                _.Name,
                                Departments = _.Departments.Select(department => department.Name).ToList()
                            }),
                    SingleQueryCollections))
            .IsEqualTo(1);

    [Test]
    public async Task SingleQueryCollectionsCountsIncludesOfAProjectedEntity() =>
        await Assert.That(
                Measure(
                    context => context.Companies
                        .Include(_ => _.Departments)
                        .ThenInclude(_ => _.Employees)
                        .Select(
                            _ => new
                            {
                                Company = _
                            }),
                    SingleQueryCollections))
            .IsEqualTo(2);

    // Entity Framework carries the ThenInclude onto the department the projection returns
    [Test]
    public async Task SingleQueryCollectionsCountsIncludesOfAProjectedNavigation() =>
        await Assert.That(
                Measure(
                    context => context.Employees
                        .Include(_ => _.Department)
                        .ThenInclude(_ => _.Employees)
                        .Select(_ => _.Department),
                    SingleQueryCollections))
            .IsEqualTo(1);

    // The method runs after the company is loaded with its Includes
    [Test]
    public async Task SingleQueryCollectionsCountsIncludesOfAnEntityPassedToAMethod() =>
        await Assert.That(
                Measure(
                    context => context.Companies
                        .Include(_ => _.Departments)
                        .ThenInclude(_ => _.Employees)
                        .Select(_ => Describe(_)),
                    SingleQueryCollections))
            .IsEqualTo(2);

    static string Describe(Company company) =>
        company.Name;

    static void SplitByDefault(DbContextOptionsBuilder<TestDbContext> builder) =>
        builder.UseSqlServer(
            "Server=.;Database=Test;",
            _ => _.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

    static async Task AssertNoCollections(
        Func<TestDbContext, IQueryable> query,
        Action<DbContextOptionsBuilder<TestDbContext>>? configure = null)
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: SingleQueryCollections(Limits.None, 0),
            configure: configure);
        query(context).ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(0);
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
