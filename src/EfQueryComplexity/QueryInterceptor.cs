/// <summary>
/// Measures a query and removes the marker calls, while the query is compiled.
/// </summary>
/// <remarks>
/// Only runs when a query shape is compiled, which is once for each distinct shape, so the cost is
/// paid once and a shape is logged once. A query that throws is never cached, so it throws again
/// every time it is used.
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

        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<QueryComplexityOptionsExtension>();
        if (extension == null)
        {
            return query;
        }

        // The markers have to go, or Entity Framework cannot translate the query
        var (stripped, ignore, @override) = MarkerReader.Strip(query);
        if (ignore)
        {
            return stripped;
        }

        var logAt = extension.LogAt.Apply(@override);
        var throwAt = extension.ThrowAt?.Apply(@override);

        if (!logAt.HasShapeLevels &&
            throwAt?.HasShapeLevels != true)
        {
            return stripped;
        }

        var shape = ShapeAnalyzer.Analyze(stripped, context.Model);

        if (throwAt != null)
        {
            var violations = Violations.ForShape(shape, throwAt);
            if (violations.Count > 0)
            {
                throw new QueryComplexityException(
                    Violations.BuildMessage(violations, ExpressionPrinter.Print(query)),
                    violations);
            }
        }

        var logViolations = Violations.ForShape(shape, logAt);
        if (logViolations.Count > 0)
        {
            ComplexityLogger.Log(
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>(),
                Violations.BuildMessage(logViolations, ExpressionPrinter.Print(query)));
        }

        return stripped;
    }
}
