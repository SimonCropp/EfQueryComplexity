/// <summary>
/// Measures a query and removes the marker calls, while the query is compiled.
/// </summary>
/// <remarks>
/// Only runs when a query shape is compiled, which is once for each distinct shape, so the cost is
/// paid once and a shape is logged once. A query that throws does so every time it is used, but
/// ComplexityQueryCompiler caches the failure in place of the query, so it is measured once too.
/// </remarks>
sealed class QueryInterceptor :
    IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression query, QueryExpressionEventData eventData)
    {
        var context = eventData.Context;
        if (context == null)
        {
            return query;
        }

        // Added by UseQueryComplexity along with this interceptor
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<QueryComplexityOptionsExtension>()!;

        // The markers have to go, or Entity Framework cannot translate the query
        var (stripped, ignore, @override) = MarkerReader.Strip(query);
        if (ignore)
        {
            return stripped;
        }

        // The value checks are only set up when the configured levels have a value level, which is
        // decided before any query exists. An override cannot turn them on afterwards, so say so
        // rather than skipping a check the query asked for.
        if (@override is {HasValueLevels: true} &&
            !extension.HasValueLevels)
        {
            throw new InvalidOperationException(
                "WithQueryComplexity sets MaxTake or MaxInValues, but neither is set in the levels passed to UseQueryComplexity, so the value checks are not registered and the override cannot be honored. Set MaxTake or MaxInValues in UseQueryComplexity, or remove them from the override.");
        }

        var logAt = extension.LogAt.Apply(@override);
        var throwAt = extension.ThrowAt?.Apply(@override);

        if (!logAt.HasShapeLevels &&
            throwAt?.HasShapeLevels != true)
        {
            return stripped;
        }

        var shape = ShapeAnalyzer.Analyze(stripped, context.Model, SplitByDefault(context));

        if (throwAt != null)
        {
            var violations = Violations.ForShape(shape, throwAt);
            if (violations.Count > 0)
            {
                throw new QueryComplexityException(
                    Violations.BuildMessage(violations, ExpressionPrinter.Print(stripped)),
                    violations);
            }
        }

        var logViolations = Violations.ForShape(shape, logAt);
        if (logViolations.Count > 0)
        {
            ComplexityLogger.Log(
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>(),
                () => Violations.BuildMessage(logViolations, ExpressionPrinter.Print(stripped)));
        }

        return stripped;
    }

    // UseQuerySplittingBehavior on the provider, for example UseSqlServer(_ => _.UseQuerySplittingBehavior(...))
    static bool SplitByDefault(DbContext context) =>
        context.GetService<IDbContextOptions>()
            .Extensions
            .OfType<RelationalOptionsExtension>()
            .FirstOrDefault()
            ?.QuerySplittingBehavior == QuerySplittingBehavior.SplitQuery;
}
