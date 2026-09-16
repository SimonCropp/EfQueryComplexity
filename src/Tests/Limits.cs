public static class Limits
{
    // Nothing is checked until a test opts in to a level
    public static readonly QueryComplexityLimits None = new(
        MaxNodes: null,
        MaxDepth: null,
        MaxOperators: null,
        MaxNavigationDepth: null,
        MaxIncludes: null,
        MaxIncludeDepth: null,
        MaxTake: null,
        MaxInValues: null,
        RejectUnbounded: false);
}
