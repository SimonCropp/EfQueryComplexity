public class MemberTypeTests
{
    [Test]
    public async Task FieldNavigationIsCounted() =>
        await Assert.That(MeasureNavigationDepth(context => context.Pets.Select(_ => _.Owner.Name)))
            .IsEqualTo(1);

    // Only Owner counts. Flags is enumerable, but it has no element type, so it is not mistaken for a
    // collection navigation.
    [Test]
    public async Task NonGenericCollectionIsNotCounted() =>
        await Assert.That(MeasureNavigationDepth(context => context.Pets.Select(_ => _.Owner.Flags)))
            .IsEqualTo(1);

    // IEnumerable<T> does not list itself among its interfaces, so the element type is read from the
    // type itself
    [Test]
    public async Task EnumerableNavigationIsCounted() =>
        await Assert.That(MeasureNavigationDepth(context => context.Owners.Select(_ => _.Pets.Count())))
            .IsEqualTo(1);

    static int MeasureNavigationDepth(Func<MemberTypeContext, IQueryable> query)
    {
        // A level of zero fires for any navigation, and the violation reports what the query measures
        using var overLevel = Build(0);
        var exception = Assert.Throws<QueryComplexityException>(() => query(overLevel).ToQueryString());
        var actual = exception.Violations.Single().Actual!.Value;

        // At that level the query translates, so it is one Entity Framework can run
        using var atLevel = Build(actual);
        query(atLevel).ToQueryString();

        return actual;
    }

    static MemberTypeContext Build(int maxNavigationDepth) =>
        new(
            new DbContextOptionsBuilder<MemberTypeContext>()
                .UseSqlServer("Server=.;Database=Test;")
                .EnableServiceProviderCaching(false)
                .UseQueryComplexity(Limits.None, Limits.None with {MaxNavigationDepth = maxNavigationDepth})
                .Options);
}
