public class KeyLookupTests
{
    [Test]
    public Task PrimaryKey()
    {
        var id = 1;
        return AssertBounded(context => context.Courses.Where(_ => _.Id == id));
    }

    [Test]
    public Task PrimaryKeyConstant() =>
        AssertBounded(context => context.Courses.Where(_ => _.Id == 1));

    [Test]
    public Task ValueOnTheLeft()
    {
        var id = 1;
        return AssertBounded(context => context.Courses.Where(_ => id == _.Id));
    }

    // The key is cast to int? to compare it
    [Test]
    public Task NullableValue()
    {
        int? id = 1;
        return AssertBounded(context => context.Courses.Where(_ => _.Id == id));
    }

    [Test]
    public Task EfProperty()
    {
        var id = 1;
        return AssertBounded(context => context.Courses.Where(_ => EF.Property<int>(_, nameof(Course.Id)) == id));
    }

    [Test]
    public Task OtherConditions()
    {
        var id = 1;
        return AssertBounded(context => context.Courses.Where(_ => _.Credits > 3 && _.Id == id));
    }

    [Test]
    public Task EitherKey()
    {
        var id = 1;
        var other = 2;
        return AssertBounded(context => context.Courses.Where(_ => _.Id == id || _.Id == other));
    }

    [Test]
    public Task KeyOrAnythingElse()
    {
        var id = 1;
        return AssertUnbounded(context => context.Courses.Where(_ => _.Id == id || _.Credits > 3));
    }

    [Test]
    public Task AlternateKey()
    {
        var code = "CS101";
        return AssertBounded(context => context.Courses.Where(_ => _.Code == code));
    }

    // A filter can make an index unique among only some of the rows
    [Test]
    public Task UniqueIndexIsNotAKey()
    {
        var title = "Algorithms";
        return AssertUnbounded(context => context.Courses.Where(_ => _.Title == title));
    }

    [Test]
    public Task NotAKey()
    {
        var credits = 3;
        return AssertUnbounded(context => context.Courses.Where(_ => _.Credits == credits));
    }

    // Every row can match
    [Test]
    public Task KeyComparedWithTheRow() =>
        AssertUnbounded(context => context.Courses.Where(_ => _.Id == _.Credits));

    [Test]
    public Task KeyRange()
    {
        var id = 1;
        return AssertUnbounded(context => context.Courses.Where(_ => _.Id > id));
    }

    [Test]
    public Task CompositeKey()
    {
        var student = 1;
        var course = 2;
        return AssertBounded(context => context.Enrollments.Where(_ => _.StudentId == student && _.CourseId == course));
    }

    [Test]
    public Task PartOfCompositeKey()
    {
        var student = 1;
        return AssertUnbounded(context => context.Enrollments.Where(_ => _.StudentId == student));
    }

    [Test]
    public Task OperatorsAround()
    {
        var id = 1;
        return AssertBounded(
            context => context.Courses
                .AsNoTracking()
                .OrderBy(_ => _.Title)
                .Where(_ => _.Id == id)
                .TagWith("Lookup"));
    }

    [Test]
    public Task DerivedType()
    {
        var id = 1;
        return AssertBounded(context => context.Courses.OfType<OnlineCourse>().Where(_ => _.Id == id));
    }

    [Test]
    public Task DerivedRoot()
    {
        var id = 1;
        return AssertBounded(context => context.Set<OnlineCourse>().Where(_ => _.Id == id));
    }

    // Each course is returned twice
    [Test]
    public Task RowsRepeated()
    {
        var id = 1;
        return AssertUnbounded(context => context.Courses.Concat(context.Courses).Where(_ => _.Id == id));
    }

    // The raw SQL can return a key more than once
    [Test]
    public Task FromSql()
    {
        var id = 1;
        return AssertUnbounded(context => context.Courses.FromSql($"select * from Courses").Where(_ => _.Id == id));
    }

    // The Id of a projection is not the key of the rows it was read from
    [Test]
    public Task KeyOfAProjection()
    {
        var id = 1;
        return AssertUnbounded(
            context => context.Courses
                .Select(_ => new Course
                {
                    Id = _.Credits
                })
                .Where(_ => _.Id == id));
    }

    [Test]
    public Task KeyInList()
    {
        var ids = new List<int> {1, 2, 3};
        return AssertBounded(context => context.Courses.Where(_ => ids.Contains(_.Id)), maxInValues: 10);
    }

    [Test]
    public Task KeyInArray()
    {
        int[] ids = [1, 2, 3];
        return AssertBounded(context => context.Courses.Where(_ => ids.Contains(_.Id)), maxInValues: 10);
    }

    // Without MaxInValues the list can hold every key
    [Test]
    public Task KeyInListWithoutMaxInValues()
    {
        var ids = new List<int> {1, 2, 3};
        return AssertUnbounded(context => context.Courses.Where(_ => ids.Contains(_.Id)));
    }

    [Test]
    public Task NotAKeyInList()
    {
        var credits = new List<int> {1, 2, 3};
        return AssertUnbounded(context => context.Courses.Where(_ => credits.Contains(_.Credits)), maxInValues: 10);
    }

    [Test]
    public Task CompositeKeyWithList()
    {
        var student = 1;
        var courses = new List<int> {1, 2, 3};
        return AssertBounded(
            context => context.Enrollments.Where(_ => _.StudentId == student && courses.Contains(_.CourseId)),
            maxInValues: 10);
    }

    // Each list is limited, but together they allow the product of their sizes
    [Test]
    public Task CompositeKeyWithTwoLists()
    {
        var students = new List<int> {1, 2, 3};
        var courses = new List<int> {1, 2, 3};
        return AssertUnbounded(
            context => context.Enrollments.Where(_ => students.Contains(_.StudentId) && courses.Contains(_.CourseId)),
            maxInValues: 10);
    }

    // A subquery is not a list the query sends, so MaxInValues does not limit it
    [Test]
    public Task KeyInSubquery() =>
        AssertUnbounded(
            context => context.Courses.Where(_ => context.Enrollments.Select(enrollment => enrollment.CourseId).Contains(_.Id)),
            maxInValues: 10);

    // Each set of levels decides for itself whether a list is limited
    [Test]
    public async Task ListBoundsOnlyTheLevelsLimitingIt()
    {
        var ids = new List<int> {1, 2, 3};
        var (context, logs) = Build(
            logAt: Limits.None with
            {
                RejectUnbounded = true
            },
            throwAt: Limits.None with
            {
                RejectUnbounded = true,
                MaxInValues = 10
            });

        context.Courses.Where(_ => ids.Contains(_.Id)).ToQueryString();

        await Assert.That(logs.Single()).Contains("RejectUnbounded");
    }

    static async Task AssertBounded(Func<KeyContext, IQueryable> query, int? maxInValues = null)
    {
        var (context, logs) = Build(Limits.None, Checked(maxInValues));
        query(context).ToQueryString();
        await Assert.That(logs.Count).IsEqualTo(0);
    }

    static async Task AssertUnbounded(Func<KeyContext, IQueryable> query, int? maxInValues = null)
    {
        var (context, _) = Build(Limits.None, Checked(maxInValues));
        var exception = Assert.Throws<QueryComplexityException>(() => query(context).ToQueryString());
        await Assert.That(exception.Violations.Single().Limit).IsEqualTo("RejectUnbounded");
    }

    static QueryComplexityLimits Checked(int? maxInValues) =>
        Limits.None with
        {
            RejectUnbounded = true,
            MaxInValues = maxInValues
        };

    static (KeyContext context, List<string> logs) Build(QueryComplexityLimits logAt, QueryComplexityLimits throwAt)
    {
        var logs = new List<string>();
        var options = new DbContextOptionsBuilder<KeyContext>()
            .UseSqlServer("Server=.;Database=Test;")
            .EnableServiceProviderCaching(false)
            .LogTo(logs.Add, [QueryComplexityEventId.LimitExceeded], LogLevel.Debug, DbContextLoggerOptions.None)
            .UseQueryComplexity(logAt, throwAt)
            .Options;
        return (new(options), logs);
    }
}
