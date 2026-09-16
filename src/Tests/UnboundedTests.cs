public class UnboundedTests
{
    [Test]
    public Task DbSet() =>
        AssertUnbounded(context => context.Employees);

    [Test]
    public Task Where() =>
        AssertUnbounded(context => context.Employees.Where(_ => _.Salary > 10));

    [Test]
    public Task SkipWithoutTake() =>
        AssertUnbounded(context => context.Employees.Skip(5));

    [Test]
    public Task Take() =>
        AssertBounded(context => context.Employees.Take(5));

    [Test]
    public Task TakeThenSelect() =>
        AssertBounded(context => context.Employees.Take(5).Select(_ => _.Name));

    [Test]
    public Task IncludeWithTake() =>
        AssertBounded(context => context.Companies.Include(_ => _.Departments).Take(5));

    [Test]
    public Task SelectManyThenTake() =>
        AssertBounded(context => context.Departments.SelectMany(_ => _.Employees).Take(5));

    // A Take below one of these bounds the source, not the query
    [Test]
    public Task TakeThenSelectMany() =>
        AssertUnbounded(context => context.Departments.Take(5).SelectMany(_ => _.Employees));

    [Test]
    public Task GroupByAfterTake() =>
        AssertBounded(context => context.Employees.Take(5).GroupBy(_ => _.DepartmentId).Select(_ => _.Key));

    [Test]
    public Task ConcatOfBounded() =>
        AssertBounded(context => context.Employees.Take(5).Concat(context.Employees.Take(5)));

    [Test]
    public Task ConcatWithUnbounded() =>
        AssertUnbounded(context => context.Employees.Take(5).Concat(context.Employees));

    [Test]
    public async Task ScalarTerminalsAreBounded()
    {
        var (context, _) = ContextBuilder.Build();
        var employees = context.Employees;

        await Assert.That(UnboundedDetector.IsUnbounded(Terminal(employees, nameof(Queryable.Count)))).IsFalse();
        await Assert.That(UnboundedDetector.IsUnbounded(Terminal(employees, nameof(Queryable.First)))).IsFalse();
        await Assert.That(UnboundedDetector.IsUnbounded(Terminal(employees, nameof(Queryable.Any)))).IsFalse();
        await Assert.That(UnboundedDetector.IsUnbounded(Terminal(employees, nameof(Queryable.LongCount)))).IsFalse();
    }

    static Expression Terminal(IQueryable<Employee> source, string name) =>
        Expression.Call(typeof(Queryable), name, [typeof(Employee)], source.Expression);

    static async Task AssertUnbounded(Func<TestDbContext, IQueryable> query)
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {RejectUnbounded = true});
        var exception = Assert.Throws<QueryComplexityException>(() => query(context).ToQueryString());
        await Assert.That(exception.Violations.Single().Limit).IsEqualTo("RejectUnbounded");
    }

    static async Task AssertBounded(Func<TestDbContext, IQueryable> query)
    {
        var (context, logs) = ContextBuilder.Build(logAt: Limits.None with {RejectUnbounded = true});
        query(context).ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(0);
    }
}
