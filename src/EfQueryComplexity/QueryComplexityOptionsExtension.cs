sealed class QueryComplexityOptionsExtension(
    QueryComplexityLimits logAt,
    QueryComplexityLimits? throwAt,
    int? sqlServerCostLimit) :
    IDbContextOptionsExtension
{
    public QueryComplexityLimits LogAt { get; } = logAt;

    public QueryComplexityLimits? ThrowAt { get; } = throwAt;

    public int? SqlServerCostLimit { get; } = sqlServerCostLimit;

    public bool HasValueLevels =>
        LogAt.HasValueLevels ||
        ThrowAt?.HasValueLevels == true;

    public DbContextOptionsExtensionInfo Info => field ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    // Throwing is evaluated before logging, so a throw level below its log level leaves that log
    // level unreachable: every query that would have logged throws instead. Equal levels are left
    // alone, since passing the same levels for both is a reasonable way to say "only throw".
    public void Validate(IDbContextOptions options)
    {
        if (ThrowAt == null)
        {
            return;
        }

        Check(nameof(QueryComplexityLimits.MaxNodes), LogAt.MaxNodes, ThrowAt.MaxNodes);
        Check(nameof(QueryComplexityLimits.MaxDepth), LogAt.MaxDepth, ThrowAt.MaxDepth);
        Check(nameof(QueryComplexityLimits.MaxOperators), LogAt.MaxOperators, ThrowAt.MaxOperators);
        Check(nameof(QueryComplexityLimits.MaxNavigationDepth), LogAt.MaxNavigationDepth, ThrowAt.MaxNavigationDepth);
        Check(nameof(QueryComplexityLimits.MaxIncludes), LogAt.MaxIncludes, ThrowAt.MaxIncludes);
        Check(nameof(QueryComplexityLimits.MaxIncludeDepth), LogAt.MaxIncludeDepth, ThrowAt.MaxIncludeDepth);
        Check(nameof(QueryComplexityLimits.MaxTake), LogAt.MaxTake, ThrowAt.MaxTake);
        Check(nameof(QueryComplexityLimits.MaxInValues), LogAt.MaxInValues, ThrowAt.MaxInValues);
    }

    static void Check(string level, int? logAt, int? throwAt)
    {
        if (logAt == null ||
            throwAt == null ||
            throwAt >= logAt)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The throwAt level for {level} ({throwAt}) is below the logAt level ({logAt}). A query is checked against throwAt first, so it would throw before it could be logged, and the logAt level is never reached. Raise throwAt above logAt, or set the logAt level to null.");
    }

    sealed class ExtensionInfo(QueryComplexityOptionsExtension extension) :
        DbContextOptionsExtensionInfo(extension)
    {
        new QueryComplexityOptionsExtension Extension => (QueryComplexityOptionsExtension) base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment
        {
            get
            {
                var builder = new StringBuilder("using QueryComplexity");

                if (Extension.ThrowAt != null)
                {
                    builder.Append(" throwAt");
                }

                if (Extension.SqlServerCostLimit != null)
                {
                    builder.Append($" sqlServerCostLimit={Extension.SqlServerCostLimit}");
                }

                builder.Append(' ');
                return builder.ToString();
            }
        }

        // The compiled query cache belongs to the internal service provider, so contexts configured
        // with different levels must not share one. Otherwise a query compiled and checked under the
        // looser levels would be reused, unchecked, by a context with stricter levels.
        public override int GetServiceProviderHashCode() =>
            HashCode.Combine(Extension.LogAt, Extension.ThrowAt, Extension.SqlServerCostLimit);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo info &&
            Extension.LogAt == info.Extension.LogAt &&
            Extension.ThrowAt == info.Extension.ThrowAt &&
            Extension.SqlServerCostLimit == info.Extension.SqlServerCostLimit;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["QueryComplexity:LogAt"] = Extension.LogAt.ToString();
            debugInfo["QueryComplexity:ThrowAt"] = Extension.ThrowAt?.ToString() ?? "null";
            debugInfo["QueryComplexity:SqlServerCostLimit"] = Extension.SqlServerCostLimit?.ToString() ?? "null";
        }
    }
}
