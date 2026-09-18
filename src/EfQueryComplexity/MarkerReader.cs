/// <summary>
/// Reads what the marker calls in a query asked for, and removes them where they have to go.
/// </summary>
/// <remarks>
/// Only the calls the query itself is built from are read. The levels are for the whole query, so a
/// marker inside a subquery would change the levels of the query containing it, and a query composed
/// over a marked one would skip checks it never asked to skip. One there is rejected rather than
/// honored, since the alternative is to drop it without saying so.
/// </remarks>
sealed class MarkerReader(List<MethodCallExpression> chain) :
    ExpressionVisitor
{
    /// <summary>
    /// Reads the markers and removes them, which Entity Framework needs before it can translate the
    /// query.
    /// </summary>
    public static (Expression Query, bool Ignore, QueryComplexityOverride? Override) Strip(Expression query)
    {
        var chain = Chain(query);
        var (ignore, @override) = Read(chain);
        var stripped = new MarkerReader(chain).Visit(query);
        return (stripped, ignore, @override);
    }

    /// <summary>
    /// Reads the markers without rebuilding anything, for a caller that only wants what they asked
    /// for.
    /// </summary>
    public static (bool Ignore, QueryComplexityOverride? Override) Read(Expression query) =>
        Read(Chain(query));

    // The calls the query itself is built from, outermost first. A marker anywhere else is inside a
    // subquery.
    static List<MethodCallExpression> Chain(Expression query)
    {
        var chain = new List<MethodCallExpression>();

        while (query is MethodCallExpression
               {
                   Method.IsStatic: true,
                   Arguments: [var source, ..]
               } call &&
               Sequences.IsSequence(source.Type))
        {
            chain.Add(call);
            query = source;
        }

        return chain;
    }

    // The outermost call is read first, so an inner call only fills levels the outer one left alone
    static (bool Ignore, QueryComplexityOverride? Override) Read(List<MethodCallExpression> chain)
    {
        var ignore = false;
        QueryComplexityOverride? merged = null;

        foreach (var call in chain)
        {
            var method = call.Method;
            if (Markers.IsIgnore(method))
            {
                ignore = true;
            }
            else if (Markers.IsOverride(method))
            {
                // The levels are read while the query is compiled, so they have to be a constant by
                // then. A parameter of a compiled query never is, since its value only exists once
                // the query runs.
                if (call.Arguments[1] is not ConstantExpression {Value: QueryComplexityOverride @override})
                {
                    throw new InvalidOperationException(
                        $"The levels given to {nameof(Markers.WithQueryComplexity)}() are read while the query is compiled, so they have to be known by then, and these are not. A parameter of a compiled query is only known once the query runs. Write the levels into the query, and use a compiled query of its own for each set of them.");
                }

                merged = Merge(merged, @override);
            }
        }

        return (ignore, merged);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var method = node.Method;

        if (Markers.IsIgnore(method))
        {
            return Remove(node, nameof(Markers.IgnoreQueryComplexity));
        }

        if (Markers.IsOverride(method))
        {
            return Remove(node, nameof(Markers.WithQueryComplexity));
        }

        // The public methods put the marker calls into a query as they run. One still in the query is
        // one Entity Framework could not run while compiling, so no marker was built to read, and the
        // query cannot be translated either.
        if (method.DeclaringType == typeof(QueryComplexityExtensions))
        {
            throw new InvalidOperationException(
                $"{method.Name}() is part of the query rather than having been applied to it, so Entity Framework could not read what it asked for while compiling the query. That is what happens inside a compiled query whose levels are a parameter, since the value only exists once the query runs. Write the levels into the query, and use a compiled query of its own for each set of them.");
        }

        return base.VisitMethodCall(node);
    }

    Expression Remove(MethodCallExpression node, string name)
    {
        if (!chain.Contains(node))
        {
            throw new InvalidOperationException(
                $"{name}() is called on a subquery of the query being executed. The levels are for the whole query, so honoring it there would change the levels of the query containing the subquery, and every query composed over that subquery would use levels it never asked for. Call {name}() on the query being executed, or remove it.");
        }

        return Visit(node.Arguments[0]);
    }

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
