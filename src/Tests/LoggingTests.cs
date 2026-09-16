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
