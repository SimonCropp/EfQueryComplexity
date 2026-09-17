/// <summary>
/// What a query measures, independent of the values it is executed with.
/// </summary>
readonly record struct QueryShape(
    int Nodes,
    int Depth,
    int Operators,
    int NavigationDepth,
    int Includes,
    int IncludeDepth,
    IReadOnlyList<Type> UnboundedTypes);
