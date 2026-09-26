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
        Add(violations, nameof(QueryComplexityLimits.MaxSingleQueryCollections), limits.MaxSingleQueryCollections, shape.SingleQueryCollections);

        // A key looked up in a list returns a row for each value in it, so only bounds the query when
        // MaxInValues limits the list
        var unboundedTypes = limits.MaxInValues == null ? shape.UnboundedTypes : shape.UnboundedTypesWhenListsLimited;
        var rowTypes = CheckedTypes(unboundedTypes, limits.RejectUnbounded);
        if (rowTypes != null)
        {
            violations.Add(
                new(nameof(QueryComplexityLimits.RejectUnbounded), null, null)
                {
                    RowTypes = rowTypes
                });
        }

        return violations;
    }

    // The unbounded row types that the levels check, or null when they check none of them
    static List<Type>? CheckedTypes(IReadOnlyList<Type> unboundedTypes, UnboundedEntities? rejectUnbounded)
    {
        if (rejectUnbounded == null)
        {
            return null;
        }

        List<Type>? checkedTypes = null;
        foreach (var type in unboundedTypes)
        {
            if (rejectUnbounded.Covers(type))
            {
                checkedTypes ??= [];
                checkedTypes.Add(type);
            }
        }

        return checkedTypes;
    }

    // Null rather than an empty list, since this runs for every execution and a query that stays
    // inside its levels should not allocate
    public static List<QueryComplexityViolation>? ForValues(int take, int inValues, QueryComplexityLimits limits)
    {
        List<QueryComplexityViolation>? violations = null;

        if (take > limits.MaxTake)
        {
            violations = [new(nameof(QueryComplexityLimits.MaxTake), limits.MaxTake, take)];
        }

        if (inValues > limits.MaxInValues)
        {
            violations ??= [];
            violations.Add(new(nameof(QueryComplexityLimits.MaxInValues), limits.MaxInValues, inValues));
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
        AppendQuery(builder, query);
        return builder.ToString();
    }

    // The query that broke a level is exactly the query that prints long, so an unbounded message
    // would put tens of kilobytes into the log line reporting it, and into the exception. The head of
    // a query is what identifies it.
    const int maxQueryLength = 1000;

    static void AppendQuery(StringBuilder builder, string query)
    {
        if (query.Length <= maxQueryLength)
        {
            builder.Append(query);
            return;
        }

        builder.Append(query, 0, maxQueryLength);
        builder.Append("… (");
        builder.Append(query.Length - maxQueryLength);
        builder.Append(" more characters)");
    }

    static string Describe(QueryComplexityViolation violation)
    {
        if (violation.Limit == nameof(QueryComplexityLimits.RejectUnbounded))
        {
            var names = string.Join(", ", violation.RowTypes!.Select(_ => _.Name));
            return $"RejectUnbounded: the query returns {names} rows without a Take";
        }

        return $"{violation.Limit}: {violation.Actual} exceeds {violation.Max}";
    }
}
