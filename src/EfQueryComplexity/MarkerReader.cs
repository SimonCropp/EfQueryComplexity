/// <summary>
/// Removes marker calls from a query, and returns what they asked for.
/// </summary>
sealed class MarkerReader :
    ExpressionVisitor
{
    bool ignore;
    QueryComplexityOverride? merged;

    public static (Expression Query, bool Ignore, QueryComplexityOverride? Override) Strip(Expression query)
    {
        var reader = new MarkerReader();
        var stripped = reader.Visit(query);
        return (stripped, reader.ignore, reader.merged);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var method = node.Method;

        if (Markers.IsIgnore(method))
        {
            ignore = true;
            return Visit(node.Arguments[0]);
        }

        if (Markers.IsOverride(method))
        {
            if (node.Arguments[1] is ConstantExpression {Value: QueryComplexityOverride @override})
            {
                merged = Merge(merged, @override);
            }

            return Visit(node.Arguments[0]);
        }

        return base.VisitMethodCall(node);
    }

    // The outermost call is visited first, so an inner call only fills levels the outer one left
    // alone
    static QueryComplexityOverride Merge(QueryComplexityOverride? outer, QueryComplexityOverride inner)
    {
        if (outer == null)
        {
            return inner;
        }

        return new()
        {
            MaxNodes = outer.MaxNodes ?? inner.MaxNodes,
            MaxDepth = outer.MaxDepth ?? inner.MaxDepth,
            MaxOperators = outer.MaxOperators ?? inner.MaxOperators,
            MaxNavigationDepth = outer.MaxNavigationDepth ?? inner.MaxNavigationDepth,
            MaxIncludes = outer.MaxIncludes ?? inner.MaxIncludes,
            MaxIncludeDepth = outer.MaxIncludeDepth ?? inner.MaxIncludeDepth,
            MaxTake = outer.MaxTake ?? inner.MaxTake,
            MaxInValues = outer.MaxInValues ?? inner.MaxInValues,
            RejectUnbounded = outer.RejectUnbounded ?? inner.RejectUnbounded
        };
    }
}
