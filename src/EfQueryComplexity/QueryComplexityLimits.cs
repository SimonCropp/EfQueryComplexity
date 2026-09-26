namespace EfQueryComplexity;

/// <summary>
/// A set of complexity levels for a query.
/// </summary>
/// <remarks>
/// The same type is used for the levels a query is logged at and the levels a query throws at. A
/// level of null disables that check, and a check fires when the measured value is greater than the
/// level.
/// </remarks>
/// <param name="MaxNodes">Maximum number of expression nodes in a query.</param>
/// <param name="MaxDepth">Maximum nesting depth of a query expression.</param>
/// <param name="MaxOperators">Maximum number of LINQ operators, including those in subqueries.</param>
/// <param name="MaxNavigationDepth">Maximum number of navigations in one member access chain.</param>
/// <param name="MaxIncludes">Maximum number of Include calls.</param>
/// <param name="MaxIncludeDepth">Maximum number of navigations in one Include chain.</param>
/// <param name="MaxSingleQueryCollections">
/// Maximum number of collections one SQL query loads, through collection Includes and collections in
/// a projection. Each multiplies the rows returned for the others. A split query counts none.
/// </param>
/// <param name="MaxTake">Maximum value passed to Take. Checked on every execution.</param>
/// <param name="MaxInValues">Maximum number of values in a list the query sends, such as a Contains list. Checked on every execution.</param>
/// <param name="RejectUnbounded">
/// The types for which a query that returns rows without a Take, or a lookup by key, fires.
/// <c>true</c> checks every type, and <c>false</c> none. A key looked up in a list, as in
/// <c>ids.Contains(_.Id)</c>, is only a limit when MaxInValues is set.
/// </param>
public sealed record QueryComplexityLimits(
    int? MaxNodes,
    int? MaxDepth,
    int? MaxOperators,
    int? MaxNavigationDepth,
    int? MaxIncludes,
    int? MaxIncludeDepth,
    int? MaxTake,
    int? MaxInValues,
    UnboundedEntities? RejectUnbounded,
    int? MaxSingleQueryCollections = null)
{
    /// <summary>
    /// The levels used for logging when none are passed to UseQueryComplexity.
    /// </summary>
    /// <remarks>
    /// Change one with a with expression, for example
    /// <c>QueryComplexityLimits.LogDefaults with { MaxTake = 500 }</c>.
    /// </remarks>
    public static QueryComplexityLimits LogDefaults { get; } = new(
        MaxNodes: 1000,
        MaxDepth: 50,
        MaxOperators: 30,
        MaxNavigationDepth: 3,
        MaxIncludes: 6,
        MaxIncludeDepth: 3,
        MaxTake: 1000,
        MaxInValues: 1000,
        RejectUnbounded: UnboundedEntities.All,
        MaxSingleQueryCollections: 1);

    // Measured while a query is compiled
    internal bool HasShapeLevels =>
        MaxNodes != null ||
        MaxDepth != null ||
        MaxOperators != null ||
        MaxNavigationDepth != null ||
        MaxIncludes != null ||
        MaxIncludeDepth != null ||
        MaxSingleQueryCollections != null ||
        RejectUnbounded is {IsNone: false};

    // Measured on every execution, since the values only exist then
    internal bool HasValueLevels =>
        MaxTake != null ||
        MaxInValues != null;

    internal QueryComplexityLimits Apply(QueryComplexityOverride? @override)
    {
        if (@override == null)
        {
            return this;
        }

        return new(
            @override.MaxNodes ?? MaxNodes,
            @override.MaxDepth ?? MaxDepth,
            @override.MaxOperators ?? MaxOperators,
            @override.MaxNavigationDepth ?? MaxNavigationDepth,
            @override.MaxIncludes ?? MaxIncludes,
            @override.MaxIncludeDepth ?? MaxIncludeDepth,
            @override.MaxTake ?? MaxTake,
            @override.MaxInValues ?? MaxInValues,
            @override.RejectUnbounded ?? RejectUnbounded,
            @override.MaxSingleQueryCollections ?? MaxSingleQueryCollections);
    }
}
