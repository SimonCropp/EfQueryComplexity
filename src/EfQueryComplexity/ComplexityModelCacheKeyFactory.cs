/// <summary>
/// Puts the levels into the key for the model cache, so contexts with different levels never share a
/// model.
/// </summary>
/// <remarks>
/// Entity Framework keys a compiled query on the model it was compiled for, so a model of its own is
/// what keeps a query that was compiled and checked under one set of levels from being handed to a
/// context with another set. Keying the internal service provider is not enough on its own: the
/// models and the compiled queries are both cached in an IMemoryCache, and UseMemoryCache gives that
/// one cache to every provider it is passed to.
/// </remarks>
sealed class ComplexityModelCacheKeyFactory(ModelCacheKeyFactoryDependencies dependencies) :
    ModelCacheKeyFactory(dependencies)
{
    // The factory IModelCacheKeyFactory is currently replaced with, or null for the Entity Framework
    // default. ReplaceService records a replacement under the service type, with no current
    // implementation.
    public static Type? Replacement(CoreOptionsExtension? core)
    {
        var replaced = core?.ReplacedServices;
        if (replaced == null)
        {
            return null;
        }

        replaced.TryGetValue((typeof(IModelCacheKeyFactory), null), out var factory);
        return factory;
    }

    public override object Create(DbContext context, bool designTime)
    {
        var key = base.Create(context, designTime);

        // Registered by UseQueryComplexity along with the extension, and left in place by a later
        // call that turns every check off
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<QueryComplexityOptionsExtension>();
        if (extension == null)
        {
            return key;
        }

        return new Key(key, extension.LogAt, extension.ThrowAt);
    }

    // The levels are records, so this compares the levels themselves rather than the instances they
    // were configured with
    sealed record Key(object Inner, QueryComplexityLimits LogAt, QueryComplexityLimits? ThrowAt);
}
