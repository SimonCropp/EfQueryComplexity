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

    static ICompiledQueryCache Cache(TestDbContext context) =>
        context.GetService<ICompiledQueryCache>();

    static TestDbContext CachedContext(QueryComplexityLimits logAt)
    {
        var (context, _) = ContextBuilder.Build(logAt: logAt, cacheServiceProvider: true);
        return context;
    }
}
