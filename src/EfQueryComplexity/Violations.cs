/// <summary>
/// Compares what a query measures against a set of levels, and describes what was exceeded.
/// </summary>
static class Violations
{
    public static List<QueryComplexityViolation> ForShape(QueryShape shape, QueryComplexityLimits limits)
    {
        var violations = new List<QueryComplexityViolation>();
        Add(violations, nameof(QueryComplexityLimits.MaxNodes), limits.MaxNodes, shape.Nodes);
        Add(violations, nameof(QueryComplexityLimits.MaxDepth), limits.MaxDepth, shape.Depth);
        Add(violations, nameof(QueryComplexityLimits.MaxOperators), limits.MaxOperators, shape.Operators);
        Add(violations, nameof(QueryComplexityLimits.MaxNavigationDepth), limits.MaxNavigationDepth, shape.NavigationDepth);
        Add(violations, nameof(QueryComplexityLimits.MaxIncludes), limits.MaxIncludes, shape.Includes);
        Add(violations, nameof(QueryComplexityLimits.MaxIncludeDepth), limits.MaxIncludeDepth, shape.IncludeDepth);

        if (limits.RejectUnbounded &&
            shape.Unbounded)
        {
            violations.Add(new(nameof(QueryComplexityLimits.RejectUnbounded), null, null));
        }

        return violations;
    }

    public static void Add(List<QueryComplexityViolation> violations, string limit, int? max, int actual)
    {
        if (actual > max)
        {
            violations.Add(new(limit, max, actual));
        }
    }

    public static string BuildMessage(IReadOnlyList<QueryComplexityViolation> violations, string query)
    {
        var builder = new StringBuilder("Query complexity limits exceeded:");

        foreach (var violation in violations)
        {
            builder.AppendLine();
            builder.Append(" * ");
            builder.Append(Describe(violation));
        }

        builder.AppendLine();
        builder.AppendLine("Change a level for this query with WithQueryComplexity(), skip every check with IgnoreQueryComplexity(), or change the levels passed to UseQueryComplexity().");
        builder.AppendLine("Query:");
        builder.Append(query);
        return builder.ToString();
    }

    static string Describe(QueryComplexityViolation violation)
    {
        if (violation.Limit == nameof(QueryComplexityLimits.RejectUnbounded))
        {
            return "RejectUnbounded: the query returns rows without a Take";
        }

        return $"{violation.Limit}: {violation.Actual} exceeds {violation.Max}";
    }
}
