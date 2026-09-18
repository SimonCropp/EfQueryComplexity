using Microsoft.Extensions.Caching.Memory;

// Entity Framework keys a compiled query on the model it was compiled for, and caches both the model
// and the compiled query in an IMemoryCache. UseMemoryCache hands that cache to more than one
// internal service provider, so keying only the service provider on the levels is not enough: the
// levels are part of the key for the model, and a model of its own is what gives each set of levels
// compiled queries of its own.
public class CacheIsolationTests
{
    [Test]
    public async Task LevelsDecideWhetherAModelIsShared()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var first = Context(cache, Limits.None with {MaxNodes = 500});
        var second = Context(cache, Limits.None with {MaxNodes = 500});
        var third = Context(cache, Limits.None with {MaxNodes = 900});

        await Assert.That(ReferenceEquals(first.Model, second.Model)).IsTrue();
        await Assert.That(ReferenceEquals(first.Model, third.Model)).IsFalse();
    }

    // The shape was measured against the levels of the context that compiled the query, so reusing
    // the query would skip the check rather than repeat it
    [Test]
    public void ShapeIsCheckedForAQueryAnotherContextCompiled()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var loose = Context(cache, Limits.None);
        loose.Employees.Where(_ => _.Salary > 10).ToQueryString();

        var strict = Context(cache, Limits.None, Limits.None with {MaxNodes = 1});
        Assert.Throws<QueryComplexityException>(() => strict.Employees.Where(_ => _.Salary > 10).ToQueryString());
    }

    // The value checks live in the delegate Entity Framework caches, and the one the looser context
    // cached has no checks in it at all
    [Test]
    public void ValuesAreCheckedForAQueryAnotherContextCompiled()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var take = 5000;

        var loose = Context(cache, Limits.None);
        loose.Employees.Take(take).ToQueryString();

        var strict = Context(cache, Limits.None, Limits.None with {MaxTake = 10});
        Assert.Throws<QueryComplexityException>(() => strict.Employees.Take(take).ToQueryString());
    }

    // And the other way around: a query that throws is cached in place of the query, so a context
    // without those levels would be handed the failure
    [Test]
    public void ACachedFailureIsNotServedToOtherLevels()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var strict = Context(cache, Limits.None, Limits.None with {MaxNodes = 1});
        Assert.Throws<QueryComplexityException>(() => strict.Employees.Where(_ => _.Salary > 20).ToQueryString());

        var loose = Context(cache, Limits.None);
        loose.Employees.Where(_ => _.Salary > 20).ToQueryString();
    }

    // EF.CompileQuery keeps the delegate it compiled for one model, rather than in the cache the rest
    // of the queries go through. Levels of their own mean a model of their own, so Entity Framework
    // refuses the reuse itself, where it used to run the query with the levels of whoever compiled it.
    [Test]
    public async Task CompiledQueryIsNotSharedAcrossLevels()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var query = EF.CompileQuery((TestDbContext data, int take) => data.Employees.Take(take));

        var loose = Context(cache, Limits.None);
        query(loose, 5000);

        var strict = Context(cache, Limits.None, Limits.None with {MaxTake = 10});
        var exception = Assert.Throws<InvalidOperationException>(() => query(strict, 5000));

        await Assert.That(exception.Message).Contains("model");
    }

    // Contexts that do share levels share the model, and the checks still run for each of them
    [Test]
    public void CompiledQueryIsCheckedForEachContextSharingLevels()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var query = EF.CompileQuery((TestDbContext data, int take) => data.Employees.Take(take));
        var throwAt = Limits.None with {MaxTake = 10};

        var first = Context(cache, Limits.None, throwAt);
        query(first, 5);

        var second = Context(cache, Limits.None, throwAt);
        Assert.Throws<QueryComplexityException>(() => query(second, 5000));
    }

    static TestDbContext Context(IMemoryCache cache, QueryComplexityLimits logAt, QueryComplexityLimits? throwAt = null)
    {
        var (context, _) = ContextBuilder.Build(logAt, throwAt, configure: _ => _.UseMemoryCache(cache));
        return context;
    }
}
