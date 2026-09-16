// The internal query compiler is the one place every execution of every query passes through.
// EF1001 is accepted here deliberately, and this file is the only place it is used.
#pragma warning disable EF1001

using Microsoft.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Checks the values a query is executed with.
/// </summary>
/// <remarks>
/// A Take count and a Contains list only exist while a query executes, so they cannot be checked by
/// an IQueryExpressionInterceptor, which only runs while a query shape is compiled. CompileQueryCore
/// returns the delegate Entity Framework caches and then runs for every execution, including for
/// compiled queries, so wrapping it catches every execution while the query is still only measured
/// once.
/// </remarks>
sealed class ComplexityQueryCompiler :
    QueryCompiler
{
    ICurrentDbContext currentContext;

    public ComplexityQueryCompiler(
        IQueryContextFactory queryContextFactory,
        ICompiledQueryCache compiledQueryCache,
        ICompiledQueryCacheKeyGenerator compiledQueryCacheKeyGenerator,
        IDatabase database,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger,
        ICurrentDbContext currentContext,
        IEvaluatableExpressionFilter evaluatableExpressionFilter,
        IModel model) :
        base(
            queryContextFactory,
            compiledQueryCache,
            compiledQueryCacheKeyGenerator,
            database,
            logger,
            currentContext,
            evaluatableExpressionFilter,
            model) =>
        this.currentContext = currentContext;

    public static void Register(DbContextOptionsBuilder builder) =>
        builder.ReplaceService<IQueryCompiler, ComplexityQueryCompiler>();

    public override Func<QueryContext, TResult> CompileQueryCore<TResult>(
        IDatabase database,
        Expression query,
        IModel model,
        bool async)
    {
        var checker = BuildChecker(query);
        var compiled = base.CompileQueryCore<TResult>(database, query, model, async);

        if (checker == null)
        {
            return compiled;
        }

        return queryContext =>
        {
            checker.Check(queryContext);
            return compiled(queryContext);
        };
    }

    ValueChecker? BuildChecker(Expression query)
    {
        var extension = currentContext.Context
            .GetService<IDbContextOptions>()
            .FindExtension<QueryComplexityOptionsExtension>();
        if (extension is not {HasValueLevels: true})
        {
            return null;
        }

        // The markers are still in the query here, since this runs before the interceptor
        var (_, ignore, @override) = MarkerReader.Strip(query);
        if (ignore)
        {
            return null;
        }

        var logAt = extension.LogAt.Apply(@override);
        var throwAt = extension.ThrowAt?.Apply(@override);

        if (!logAt.HasValueLevels &&
            throwAt?.HasValueLevels != true)
        {
            return null;
        }

        var plan = ValuePlan.Build(query);
        if (plan.IsEmpty)
        {
            return null;
        }

        return new(plan, logAt, throwAt, query);
    }
}
