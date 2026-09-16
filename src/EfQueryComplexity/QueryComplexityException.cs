namespace EfQueryComplexity;

/// <summary>
/// Thrown when a query exceeds one of the levels passed as throwAt to UseQueryComplexity.
/// </summary>
public class QueryComplexityException :
    Exception
{
    internal QueryComplexityException(string message, IReadOnlyList<QueryComplexityViolation> violations) :
        base(message) =>
        Violations = violations;

    /// <summary>
    /// The levels that were exceeded.
    /// </summary>
    public IReadOnlyList<QueryComplexityViolation> Violations { get; }
}
