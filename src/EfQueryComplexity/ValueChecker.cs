/// <summary>
/// Checks the values a compiled query is executed with.
/// </summary>
sealed class ValueChecker(
    ValuePlan plan,
    QueryComplexityLimits logAt,
    QueryComplexityLimits? throwAt,
    Expression query)
{
    readonly List<string> logged = [];
    string? printed;

    public void Check(QueryContext queryContext)
    {
        var parameters = queryContext.Parameters;

        if (throwAt != null)
        {
            var violations = plan.Evaluate(parameters, throwAt);
            if (violations.Count > 0)
            {
                throw new QueryComplexityException(
                    Violations.BuildMessage(violations, Print()),
                    violations);
            }
        }

        var logViolations = plan.Evaluate(parameters, logAt);
        if (logViolations.Count == 0)
        {
            return;
        }

        // Values are checked for every execution, so a hot query would otherwise log the same
        // message endlessly. Each level is logged the first time a compiled query exceeds it.
        var fresh = new List<QueryComplexityViolation>();

        lock (logged)
        {
            foreach (var violation in logViolations)
            {
                if (logged.Contains(violation.Limit))
                {
                    continue;
                }

                logged.Add(violation.Limit);
                fresh.Add(violation);
            }
        }

        if (fresh.Count == 0)
        {
            return;
        }

        ComplexityLogger.Log(queryContext.QueryLogger, Violations.BuildMessage(fresh, Print()));
    }

    string Print() => printed ??= ExpressionPrinter.Print(query);
}
