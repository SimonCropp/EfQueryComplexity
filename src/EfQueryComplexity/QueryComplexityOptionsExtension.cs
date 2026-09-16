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

    public void Validate(IDbContextOptions options)
    {
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
