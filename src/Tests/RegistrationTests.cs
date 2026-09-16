// ICompiledQueryCache is used to prove that contexts with different levels do not share a compiled
// query cache
#pragma warning disable EF1001

using Microsoft.EntityFrameworkCore.Query.Internal;

public class RegistrationTests
{
    [Test]
    public async Task GenericOverloadKeepsTheTypedBuilder()
    {
        var builder = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer("Server=.;Database=Test;")
            .UseQueryComplexity();

        await Assert.That(builder.Options.FindExtension<QueryComplexityOptionsExtension>()).IsNotNull();
    }

    [Test]
    public async Task CallingTwiceRegistersOneInterceptor()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer("Server=.;Database=Test;")
            .UseQueryComplexity()
            .UseQueryComplexity(sqlServerCostLimit: 10)
            .Options;

        var core = options.FindExtension<CoreOptionsExtension>()!;

        await Assert.That(core.SingletonInterceptors!.Count(_ => _ is QueryInterceptor)).IsEqualTo(1);
        await Assert.That(core.Interceptors!.Count(_ => _ is CostLimitInterceptor)).IsEqualTo(1);
    }

    [Test]
    public async Task LevelsDecideWhetherAServiceProviderIsShared()
    {
        var first = CachedContext(Limits.None with {MaxNodes = 500});
        var second = CachedContext(Limits.None with {MaxNodes = 500});
        var third = CachedContext(Limits.None with {MaxNodes = 900});

        await Assert.That(ReferenceEquals(Cache(first), Cache(second))).IsTrue();
        await Assert.That(ReferenceEquals(Cache(first), Cache(third))).IsFalse();
    }

    [Test]
    public Task LogFragment()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer("Server=.;Database=Test;")
            .UseQueryComplexity(
                throwAt: Limits.None with {MaxNodes = 10},
                sqlServerCostLimit: 300)
            .Options;

        return Verify(options.FindExtension<QueryComplexityOptionsExtension>()!.Info.LogFragment);
    }

    [Test]
    public void NegativeCostLimitThrows() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DbContextOptionsBuilder<TestDbContext>().UseQueryComplexity(sqlServerCostLimit: -1));

    // SQL Server reads zero as the query governor being off, so accepting it would turn the check
    // off for anyone who meant to refuse everything
    [Test]
    public async Task ZeroCostLimitThrows()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new DbContextOptionsBuilder<TestDbContext>().UseQueryComplexity(sqlServerCostLimit: 0));

        await Assert.That(exception.Message).Contains("greater than zero");
    }

    // Throwing is checked first, so a throw level under its log level leaves the log level with
    // nothing to report
    [Test]
    public async Task ThrowLevelUnderLogLevelIsRejected()
    {
        // Entity Framework validates the options as the context is constructed, so this is as early
        // as it can be found
        var exception = Assert.Throws<InvalidOperationException>(
            () => ContextBuilder.Build(
                logAt: Limits.None with {MaxNodes = 1000},
                throwAt: Limits.None with {MaxNodes = 100}));

        await Assert.That(exception.Message).Contains("MaxNodes");
    }

    // The same levels for both is how to say "only throw", so it is left alone
    [Test]
    public void EqualLevelsAreAllowed()
    {
        var (context, _) = ContextBuilder.Build(
            logAt: Limits.None with {MaxNodes = 1000},
            throwAt: Limits.None with {MaxNodes = 1000});

        context.Employees.ToQueryString();
    }

    static ICompiledQueryCache Cache(TestDbContext context) =>
        context.GetService<ICompiledQueryCache>();

    static TestDbContext CachedContext(QueryComplexityLimits logAt)
    {
        var (context, _) = ContextBuilder.Build(logAt: logAt, cacheServiceProvider: true);
        return context;
    }
}
