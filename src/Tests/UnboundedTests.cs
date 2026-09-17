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

        await Assert.That(UnboundedDetector.Find(Terminal(employees, nameof(Queryable.Count)))).IsEmpty();
        await Assert.That(UnboundedDetector.Find(Terminal(employees, nameof(Queryable.First)))).IsEmpty();
        await Assert.That(UnboundedDetector.Find(Terminal(employees, nameof(Queryable.Any)))).IsEmpty();
        await Assert.That(UnboundedDetector.Find(Terminal(employees, nameof(Queryable.LongCount)))).IsEmpty();
    }

    // A projection does not change which rows are read
    [Test]
    public Task ProjectionReportsTheEntity() =>
        AssertRowTypes(
            context => context.Employees.Select(_ => new {_.Name}),
            "Employee");

    [Test]
    public Task SelectManyReportsBothTypes() =>
        AssertRowTypes(
            context => context.Departments.SelectMany(_ => _.Employees),
            "Department, Employee");

    [Test]
    public Task TakeThenSelectManyReportsTheJoinedType() =>
        AssertRowTypes(
            context => context.Departments.Take(5).SelectMany(_ => _.Employees),
            "Employee");

    [Test]
    public Task SelectManyLooksThroughOperators() =>
        AssertRowTypes(
            context => context.Departments
                .Take(5)
                .SelectMany(_ => _.Employees.Where(_ => _.Salary > 10).Select(_ => _.Name)),
            "Employee");

    [Test]
    public Task NestedSelectManyReportsEveryType() =>
        AssertRowTypes(
            context => context.Departments
                .Take(5)
                .SelectMany(_ => _.Employees.SelectMany(_ => _.Tasks)),
            "Employee, EmployeeTask");

    // The SelectMany returns more rows than the joined sequence, whatever limits that sequence
    [Test]
    public Task TakeInsideSelectManyDoesNotBound() =>
        AssertRowTypes(
            context => context.Departments.Take(5).SelectMany(_ => _.Employees.Take(5)),
            "Employee");

    [Test]
    public Task CastInsideSelectManyIsLookedThrough() =>
        AssertRowTypes(
            context => context.Departments
                .Take(5)
                .SelectMany(_ => (List<EmployeeTask>) _.Employees.SelectMany(_ => _.Tasks)),
            "Employee, EmployeeTask");

    [Test]
    public Task PropertyInsideSelectMany() =>
        AssertRowTypes(
            context => context.Departments
                .Take(5)
                .SelectMany(_ => EF.Property<List<Employee>>(_, nameof(Department.Employees))),
            "Employee");

    [Test]
    public Task DbSetInsideSelectMany() =>
        AssertRowTypes(
            context => context.Departments
                .Take(5)
                .SelectMany(_ => context.Employees.Where(employee => employee.DepartmentId == _.Id)),
            "Employee");

    [Test]
    public Task MethodGroupInsideSelectMany() =>
        AssertRowTypes(
            context => context.Departments
                .Take(5)
                .SelectMany(_ => _.Employees.SelectMany(TasksOf)),
            "Employee, EmployeeTask");

    static IEnumerable<EmployeeTask> TasksOf(Employee employee) =>
        employee.Tasks;

    [Test]
    public Task JoinReportsBothTypes() =>
        AssertRowTypes(
            context => context.Departments.Join(
                context.Employees,
                _ => _.Id,
                _ => _.DepartmentId,
                (_, employee) => employee),
            "Department, Employee");

    [Test]
    public Task TakeThenJoinReportsTheJoinedType() =>
        AssertRowTypes(
            context => context.Departments.Take(5).Join(
                context.Employees.Take(5),
                _ => _.Id,
                _ => _.DepartmentId,
                (_, employee) => employee),
            "Employee");

    [Test]
    public Task LeftJoinReportsBothTypes() =>
        AssertRowTypes(
            context => context.Departments.LeftJoin(
                context.Employees,
                _ => _.Id,
                _ => _.DepartmentId,
                (_, employee) => employee),
            "Department, Employee");

    // The grouping each department is joined with is typed IEnumerable<Employee>
    [Test]
    public Task GroupJoinIntoDefaultIfEmpty() =>
        AssertRowTypes(
            context =>
                from department in context.Departments
                join employee in context.Employees
                    on department.Id equals employee.DepartmentId into employees
                from employee in employees.DefaultIfEmpty()
                select department.Name,
            "Department, Employee");

    [Test]
    public Task SelectManyOverGroups() =>
        AssertRowTypes(
            context => context.Employees.GroupBy(_ => _.DepartmentId).SelectMany(_ => _),
            "Employee");

    [Test]
    public Task ConcatReportsBothSides() =>
        AssertRowTypes(
            context => context.Departments
                .Select(_ => _.Name)
                .Concat(context.Employees.Select(_ => _.Name)),
            "Department, Employee");

    [Test]
    public Task ValuesReportTheirType() =>
        AssertRowTypes(
            context => context.Database.SqlQuery<int>($"select 1 as Value"),
            "Int32");

    [Test]
    public Task Message()
    {
        var (context, _) = ContextBuilder.Build(
            throwAt: Limits.None with
            {
                RejectUnbounded = UnboundedEntities.AllExcept(typeof(Employee))
            });

        return Throws(() => context.Departments.SelectMany(_ => _.Employees).ToQueryString())
            .IgnoreStackTrace();
    }

    static Expression Terminal(IQueryable<Employee> source, string name) =>
        Expression.Call(typeof(Queryable), name, [typeof(Employee)], source.Expression);

    static async Task AssertUnbounded(Func<TestDbContext, IQueryable> query)
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {RejectUnbounded = true});
        var exception = Assert.Throws<QueryComplexityException>(() => query(context).ToQueryString());
        await Assert.That(exception.Violations.Single().Limit).IsEqualTo("RejectUnbounded");
    }

    static async Task AssertRowTypes(Func<TestDbContext, IQueryable> query, string expected)
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {RejectUnbounded = true});
        var exception = Assert.Throws<QueryComplexityException>(() => query(context).ToQueryString());
        var violation = exception.Violations.Single();
        await Assert.That(violation.Limit).IsEqualTo("RejectUnbounded");
        await Assert.That(string.Join(", ", violation.RowTypes!.Select(_ => _.Name))).IsEqualTo(expected);
    }

    static async Task AssertBounded(Func<TestDbContext, IQueryable> query)
    {
        var (context, logs) = ContextBuilder.Build(logAt: Limits.None with {RejectUnbounded = true});
        query(context).ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(0);
    }
}
