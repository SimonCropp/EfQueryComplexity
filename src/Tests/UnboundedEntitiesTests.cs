public class UnboundedEntitiesTests
{
    interface ISmall;

    class Animal;

    class Dog :
        Animal,
        ISmall;

    class Cat :
        Animal;

    [Test]
    public async Task OrderAndDuplicatesAreIgnored()
    {
        var first = UnboundedEntities.AllExcept(typeof(Employee), typeof(Department));
        var second = UnboundedEntities.AllExcept(typeof(Department), typeof(Employee), typeof(Department));

        await Assert.That(first == second).IsTrue();
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
        await Assert.That(first.ToString()).IsEqualTo(second.ToString());
    }

    [Test]
    public async Task NoTypes()
    {
        await Assert.That(UnboundedEntities.AllExcept()).IsSameReferenceAs(UnboundedEntities.All);
        await Assert.That(UnboundedEntities.Only()).IsSameReferenceAs(UnboundedEntities.None);
    }

    [Test]
    public async Task AllExceptAndOnlyDiffer()
    {
        var allExcept = UnboundedEntities.AllExcept(typeof(Employee));
        var only = UnboundedEntities.Only(typeof(Employee));

        await Assert.That(allExcept != only).IsTrue();
    }

    [Test]
    public async Task ConvertsFromBool()
    {
        UnboundedEntities on = true;
        UnboundedEntities off = false;

        await Assert.That(on).IsSameReferenceAs(UnboundedEntities.All);
        await Assert.That(off).IsSameReferenceAs(UnboundedEntities.None);
    }

    [Test]
    public async Task Descriptions()
    {
        await Assert.That(UnboundedEntities.All.ToString()).IsEqualTo("All");
        await Assert.That(UnboundedEntities.None.ToString()).IsEqualTo("None");
        await Assert.That(UnboundedEntities.AllExcept(typeof(Employee), typeof(Department)).ToString())
            .IsEqualTo("AllExcept(Department, Employee)");
        await Assert.That(UnboundedEntities.Only(typeof(Employee)).ToString())
            .IsEqualTo("Only(Employee)");
    }

    [Test]
    public void NullTypesThrow() =>
        Assert.Throws<ArgumentNullException>(() => UnboundedEntities.Only(null!));

    [Test]
    public void NullTypeThrows() =>
        Assert.Throws<ArgumentException>(() => UnboundedEntities.Only(typeof(Employee), null!));

    [Test]
    public void OpenGenericTypeThrows() =>
        Assert.Throws<ArgumentException>(() => UnboundedEntities.AllExcept(typeof(List<>)));

    [Test]
    public async Task AllExceptSkipsDerivedTypes()
    {
        var entities = UnboundedEntities.AllExcept(typeof(Animal));

        await Assert.That(entities.Covers(typeof(Animal))).IsFalse();
        await Assert.That(entities.Covers(typeof(Dog))).IsFalse();
        await Assert.That(entities.Covers(typeof(Employee))).IsTrue();
    }

    // A query on the base type also returns rows of the types that are not skipped
    [Test]
    public async Task AllExceptOfDerivedTypeChecksBaseType()
    {
        var entities = UnboundedEntities.AllExcept(typeof(Dog));

        await Assert.That(entities.Covers(typeof(Dog))).IsFalse();
        await Assert.That(entities.Covers(typeof(Animal))).IsTrue();
    }

    [Test]
    public async Task AllExceptSkipsImplementations()
    {
        var entities = UnboundedEntities.AllExcept(typeof(ISmall));

        await Assert.That(entities.Covers(typeof(Dog))).IsFalse();
        await Assert.That(entities.Covers(typeof(Cat))).IsTrue();
    }

    [Test]
    public async Task OnlyChecksDerivedTypes()
    {
        var entities = UnboundedEntities.Only(typeof(Animal));

        await Assert.That(entities.Covers(typeof(Dog))).IsTrue();
        await Assert.That(entities.Covers(typeof(Employee))).IsFalse();
    }

    // A query on the base type also returns rows of the checked type
    [Test]
    public async Task OnlyOfDerivedTypeChecksBaseType()
    {
        var entities = UnboundedEntities.Only(typeof(Dog));

        await Assert.That(entities.Covers(typeof(Animal))).IsTrue();
        await Assert.That(entities.Covers(typeof(Cat))).IsFalse();
    }

    [Test]
    public async Task OnlyChecksImplementations()
    {
        var entities = UnboundedEntities.Only(typeof(ISmall));

        await Assert.That(entities.Covers(typeof(Dog))).IsTrue();
        await Assert.That(entities.Covers(typeof(Cat))).IsFalse();
    }

    [Test]
    public async Task SameTypesMakeEqualLevels()
    {
        var first = Limits.None with
        {
            RejectUnbounded = UnboundedEntities.Only(typeof(Employee), typeof(Department))
        };
        var second = Limits.None with
        {
            RejectUnbounded = UnboundedEntities.Only(typeof(Department), typeof(Employee))
        };

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    [Test]
    public async Task AllExceptSkipsListedTypes()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with
            {
                RejectUnbounded = UnboundedEntities.AllExcept(typeof(Employee))
            });

        context.Employees.ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(0);

        context.Departments.ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(1);
    }

    // A joined type returns unbounded rows too, so it has to be listed as well
    [Test]
    public async Task JoinedTypeIsChecked()
    {
        var (context, _) = ContextBuilder.Build(
            throwAt: Limits.None with
            {
                RejectUnbounded = UnboundedEntities.AllExcept(typeof(Department))
            });

        var exception = Assert.Throws<QueryComplexityException>(
            () => context.Departments.SelectMany(_ => _.Employees).ToQueryString());

        await Assert.That(exception.Violations.Single().RowTypes!.Single()).IsEqualTo(typeof(Employee));
    }

    [Test]
    public async Task JoinedTypeCanBeSkipped()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with
            {
                RejectUnbounded = UnboundedEntities.AllExcept(typeof(Department), typeof(Employee))
            });

        context.Departments.SelectMany(_ => _.Employees).ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task OnlyChecksListedTypes()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with
            {
                RejectUnbounded = UnboundedEntities.Only(typeof(Employee))
            });

        context.Departments.ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(0);

        context.Employees.ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(1);
    }

    // Rows that are not entities are checked by AllExcept, since they are not listed, and not by Only
    [Test]
    public async Task ValuesAreCheckedByAllExcept()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with
            {
                RejectUnbounded = UnboundedEntities.AllExcept(typeof(Employee))
            });

        context.Database.SqlQuery<int>($"select 1 as Value").ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ValuesAreNotCheckedByOnly()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with
            {
                RejectUnbounded = UnboundedEntities.Only(typeof(Employee))
            });

        context.Database.SqlQuery<int>($"select 1 as Value").ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task NullTurnsTheCheckOff()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with
            {
                RejectUnbounded = null
            });

        context.Employees.ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task LogAndThrowCanNameDifferentTypes()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with
            {
                RejectUnbounded = UnboundedEntities.AllExcept(typeof(Company))
            },
            throwAt: Limits.None with
            {
                RejectUnbounded = UnboundedEntities.Only(typeof(Employee))
            });

        Assert.Throws<QueryComplexityException>(() => context.Employees.ToQueryString());
        await Assert.That(logs.Count).IsEqualTo(0);

        context.Departments.ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(1);

        context.Companies.ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(1);
    }
}
