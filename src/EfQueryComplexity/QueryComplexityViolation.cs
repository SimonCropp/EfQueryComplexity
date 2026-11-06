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

    /// <summary>
    /// For MaxSingleQueryCollections, the collections the query loads side by side, in query order,
    /// each named as the entity it is from and the navigations to it, for example
    /// <c>Department.Employees</c>. Null for every other level.
    /// </summary>
    public IReadOnlyList<string>? Collections { get; init; }
}
