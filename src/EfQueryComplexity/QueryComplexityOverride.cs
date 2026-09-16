namespace EfQueryComplexity;

/// <summary>
/// Levels that replace the configured ones for a single query.
/// </summary>
/// <remarks>
/// Passed to WithQueryComplexity. Every level left null keeps the configured value. A level that is
/// set replaces both the log level and the throw level for that check, but it never enables throwing
/// for a context that was not given throw levels. To turn off one check for a query use
/// <c>int.MaxValue</c>, and to skip every check use IgnoreQueryComplexity.
/// </remarks>
public sealed record QueryComplexityOverride
{
    /// <summary>Maximum number of expression nodes in a query.</summary>
    public int? MaxNodes { get; init; }

    /// <summary>Maximum nesting depth of a query expression.</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Maximum number of LINQ operators, including those in subqueries.</summary>
    public int? MaxOperators { get; init; }

    /// <summary>Maximum number of navigations in one member access chain.</summary>
    public int? MaxNavigationDepth { get; init; }

    /// <summary>Maximum number of Include calls.</summary>
    public int? MaxIncludes { get; init; }

    /// <summary>Maximum number of navigations in one Include chain.</summary>
    public int? MaxIncludeDepth { get; init; }

    /// <summary>Maximum value passed to Take.</summary>
    public int? MaxTake { get; init; }

    /// <summary>Maximum number of values in a Contains list.</summary>
    public int? MaxInValues { get; init; }

    /// <summary>Whether a query that returns rows without a Take fires.</summary>
    public bool? RejectUnbounded { get; init; }
}
