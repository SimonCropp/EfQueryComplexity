namespace EfQueryComplexity;

/// <summary>
/// A complexity level that a query exceeded.
/// </summary>
/// <param name="Limit">The name of the level, for example MaxNodes.</param>
/// <param name="Max">The configured level, or null for RejectUnbounded.</param>
/// <param name="Actual">The measured value, or null for RejectUnbounded.</param>
public sealed record QueryComplexityViolation(string Limit, int? Max, int? Actual);
