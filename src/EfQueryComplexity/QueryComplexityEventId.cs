namespace EfQueryComplexity;

/// <summary>
/// Events logged by EfQueryComplexity.
/// </summary>
public static class QueryComplexityEventId
{
    /// <summary>
    /// A query exceeded one of the levels passed as logAt to UseQueryComplexity.
    /// </summary>
    /// <remarks>
    /// Logged as a warning, and configurable like any other Entity Framework event, for example
    /// <c>ConfigureWarnings(_ =&gt; _.Throw(QueryComplexityEventId.LimitExceeded))</c> or
    /// <c>ConfigureWarnings(_ =&gt; _.Ignore(QueryComplexityEventId.LimitExceeded))</c>.
    /// </remarks>
    public static readonly EventId LimitExceeded = new(1_000_000, "EfQueryComplexity.LimitExceeded");
}
