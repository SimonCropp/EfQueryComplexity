namespace EfQueryComplexity;

/// <summary>
/// A complexity level that a query exceeded.
/// </summary>
/// <param name="Limit">The name of the level, for example MaxNodes.</param>
/// <param name="Max">The configured level, or null for RejectUnbounded.</param>
/// <param name="Actual">The measured value, or null for RejectUnbounded.</param>
public sealed record QueryComplexityViolation(string Limit, int? Max, int? Actual)
{
    /// <summary>
    /// For RejectUnbounded, the checked types of the rows the query returns without a Take. Null for
    /// every other level.
    /// </summary>
    public IReadOnlyList<Type>? RowTypes { get; init; }
}
