/// <summary>
/// Reads what the marker calls in a query asked for, and removes them where they have to go.
/// </summary>
sealed class MarkerReader(bool strip) :
    ExpressionVisitor
{
    bool ignore;
    QueryComplexityOverride? merged;

    /// <summary>
    /// Reads the markers and removes them, which Entity Framework needs before it can translate the
    /// query.
    /// </summary>
    public static (Expression Query, bool Ignore, QueryComplexityOverride? Override) Strip(Expression query)
    {
        var reader = new MarkerReader(true);
        var stripped = reader.Visit(query);
        return (stripped, reader.ignore, reader.merged);
    }

    /// <summary>
    /// Reads the markers without rebuilding anything, for a caller that only wants what they asked
    /// for.
    /// </summary>
    public static (bool Ignore, QueryComplexityOverride? Override) Read(Expression query)
    {
        var reader = new MarkerReader(false);
        reader.Visit(query);
        return (reader.ignore, reader.merged);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var method = node.Method;

        if (Markers.IsIgnore(method))
        {
            ignore = true;

            if (strip)
            {
                return Visit(node.Arguments[0]);
            }
        }
        else if (Markers.IsOverride(method))
        {
            if (node.Arguments[1] is ConstantExpression {Value: QueryComplexityOverride @override})
            {
                merged = Merge(merged, @override);
            }

            if (strip)
            {
                return Visit(node.Arguments[0]);
            }
        }

        // Nothing is changed when not stripping, so the visit returns the nodes it was given
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
