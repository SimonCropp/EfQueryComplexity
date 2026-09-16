public class LoggingTests
{
    [Test]
    public Task LogDefaults() =>
        Verify(QueryComplexityLimits.LogDefaults);

    [Test]
    public Task ShapeMessage()
    {
        var (context, logs) = ContextBuilder.Build(logAt: Limits.None with {MaxNodes = 1});

        context.Employees.Where(_ => _.Salary > 10).ToQueryString();

        return Verify(logs);
    }

    [Test]
    public Task ValueMessage()
    {
        var (context, logs) = ContextBuilder.Build(logAt: Limits.None with {MaxTake = 10});

        context.Employees.Take(5000).ToQueryString();

        return Verify(logs);
    }

    [Test]
    public async Task ConfigureWarningsCanThrow()
    {
        var (context, _) = ContextBuilder.Build(
            logAt: Limits.None with {MaxNodes = 1},
            configure: builder => builder.ConfigureWarnings(_ => _.Throw(QueryComplexityEventId.LimitExceeded)));

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Employees.Where(_ => _.Salary > 10).ToQueryString());

        await Assert.That(exception.Message).Contains("QueryComplexityEventId.LimitExceeded");
    }

    // A value is only known while a query runs, so an escalated value violation has to throw for
    // every execution, rather than only for the execution that would have logged it
    [Test]
    public async Task ConfigureWarningsThrowsForEveryValueExecution()
    {
        var (context, _) = ContextBuilder.Build(
            logAt: Limits.None with {MaxTake = 10},
            configure: builder => builder.ConfigureWarnings(_ => _.Throw(QueryComplexityEventId.LimitExceeded)));

        var take = 5000;

        Assert.Throws<InvalidOperationException>(() => context.Employees.Take(take).ToQueryString());

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Employees.Take(take).ToQueryString());

        await Assert.That(exception.Message).Contains("QueryComplexityEventId.LimitExceeded");
    }

    // The query that broke a level is the one that prints long, so the message bounds it rather than
    // putting the whole tree in every log line that reports it
    [Test]
    public async Task LongQueryIsTruncated()
    {
        var (context, _) = ContextBuilder.Build(throwAt: Limits.None with {MaxNodes = 1});

        var query = context.Employees.AsQueryable();
        for (var index = 0; index < 100; index++)
        {
            query = query.Where(_ => _.Salary > 10);
        }

        var exception = Assert.Throws<QueryComplexityException>(() => query.ToQueryString());

        await Assert.That(exception.Message).Contains("more characters)");
        await Assert.That(exception.Message.Length).IsLessThan(1500);
    }

    [Test]
    public async Task ConfigureWarningsCanIgnore()
    {
        var (context, logs) = ContextBuilder.Build(
            logAt: Limits.None with {MaxNodes = 1},
            configure: builder => builder.ConfigureWarnings(_ => _.Ignore(QueryComplexityEventId.LimitExceeded)));

        context.Employees.Where(_ => _.Salary > 10).ToQueryString();

        await Assert.That(logs.Count).IsEqualTo(0);
    }
}
