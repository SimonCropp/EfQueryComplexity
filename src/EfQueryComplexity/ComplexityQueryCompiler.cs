// The internal query compiler is the one place every execution of every query passes through.
// EF1001 is accepted here deliberately, and this file is the only place it is used.
#pragma warning disable EF1001

using Microsoft.EntityFrameworkCore.Query.Internal;

/// <summary>
/// Checks the values a query is executed with, and caches a query that throws.
/// </summary>
/// <remarks>
/// A Take count and a list of values only exist while a query executes, so they cannot be checked by
/// an IQueryExpressionInterceptor, which only runs while a query shape is compiled. CompileQueryCore
/// returns the delegate Entity Framework caches and then runs for every execution, including for
/// compiled queries, so wrapping it catches every execution while the query is still only measured
/// once.
///
/// A query over a throw level throws while it is compiled, and Entity Framework does not cache a
/// query that fails to compile. Returning a delegate that throws instead lets it cache the failure
/// like any other query.
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
        Func<QueryContext, TResult> compiled;
        try
        {
            compiled = base.CompileQueryCore<TResult>(database, query, model, async);
        }
        catch (QueryComplexityException exception)
        {
            return Throw<TResult>(exception);
        }

        var checker = BuildChecker(query);
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

    // Returned in place of the query, so Entity Framework caches the failure, and later executions
    // throw without the query being compiled, measured and printed again. Each execution gets its own
    // exception, since one instance thrown on several threads at once would share a stack trace.
    static Func<QueryContext, TResult> Throw<TResult>(QueryComplexityException exception)
    {
        var message = exception.Message;
        var violations = exception.Violations;
        return _ => throw new QueryComplexityException(message, violations);
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

        // The interceptor removes the markers from its own copy of the query, so they are still in
        // this one. They are only read, since a tree built here would be thrown away.
        var (ignore, @override) = MarkerReader.Read(query);
        if (ignore)
        {
            return null;
        }

        var logAt = extension.LogAt.Apply(@override);
        var throwAt = extension.ThrowAt?.Apply(@override);

        var plan = ValuePlan.Build(query);
        if (plan.IsEmpty)
        {
            return null;
        }

        return new(plan, logAt, throwAt, query);
    }
}
