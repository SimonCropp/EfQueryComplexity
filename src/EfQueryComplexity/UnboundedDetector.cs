/// <summary>
/// Decides whether a query can return an unlimited number of rows.
/// </summary>
static class UnboundedDetector
{
    public static bool IsUnbounded(Expression query)
    {
        // A query that does not return a sequence returns one row, an aggregate, or a row count
        if (!typeof(IQueryable).IsAssignableFrom(query.Type))
        {
            return false;
        }

        return !IsBounded(query);
    }

    static bool IsBounded(Expression expression)
    {
        while (expression is MethodCallExpression
               {
                   Method.IsStatic: true,
                   Arguments.Count: > 0
               } call)
        {
            var arguments = call.Arguments;
            if (call.Method.DeclaringType == typeof(Queryable))
            {
                switch (call.Method.Name)
                {
                    case "Take":
                        return true;

                    // These return more rows than their source, so a Take below one of them bounds
                    // the source rather than the query
                    case "SelectMany":
                    case "Join":
                    case "GroupJoin":
                    case "LeftJoin":
                    case "RightJoin":
                    case "Zip":
                        return false;

                    case "Concat":
                    case "Union":
                    case "UnionBy":
                        return IsBounded(arguments[0]) &&
                               IsBounded(arguments[1]);
                }
            }

            // Everything else returns no more rows than its source, so keep walking towards the root
            expression = arguments[0];
        }

        return false;
    }
}
