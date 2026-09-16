/// <summary>
/// Checks the values a compiled query is executed with.
/// </summary>
sealed class ValueChecker(
    ValuePlan plan,
    QueryComplexityLimits logAt,
    QueryComplexityLimits? throwAt,
    Expression query)
{
    List<string> logged = [];
    string? printed;

    // Whether a value has to be read at all is settled while the query is compiled
    bool checkTake = logAt.MaxTake != null ||
                     throwAt?.MaxTake != null;

    bool checkInValues = logAt.MaxInValues != null ||
                         throwAt?.MaxInValues != null;

    public void Check(QueryContext queryContext)
    {
        var parameters = queryContext.Parameters;

        // This runs for every execution, so each value is read once and then compared against both
        // sets of levels
        var take = checkTake ? plan.LargestTake(parameters) : 0;
        var inValues = checkInValues ? plan.LargestInValues(parameters) : 0;

        if (throwAt != null)
        {
            var violations = Violations.ForValues(take, inValues, throwAt);
            if (violations != null)
            {
                throw new QueryComplexityException(
                    Violations.BuildMessage(violations, Print()),
                    violations);
            }
        }

        var logViolations = Violations.ForValues(take, inValues, logAt);
        if (logViolations == null)
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

        try
        {
            ComplexityLogger.Log(queryContext.QueryLogger, () => Violations.BuildMessage(fresh, Print()));
        }
        catch
        {
            // ConfigureWarnings can turn this event into an error. Nothing was logged, so the levels
            // are left for the next execution to find, or only the first execution would throw.
            lock (logged)
            {
                foreach (var violation in fresh)
                {
                    logged.Remove(violation.Limit);
                }
            }

            throw;
        }
    }

    string Print() => printed ??= ExpressionPrinter.Print(query);
}
