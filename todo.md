# Todo

Findings from a review on 2026-09-16. 1 to 10 are fixed, 11 turned out not to be real, and 12 is
what is left.

## Performance

### 12. Smaller ones

- `NavigationsInChain` runs for every member and `ChainDepth` for every Include, so both are
  quadratic in the length of a chain.
  [src/EfQueryComplexity/ShapeAnalyzer.cs:81](src/EfQueryComplexity/ShapeAnalyzer.cs#L81)
- `Segments` allocates an array through `Split('.')` to count segments.
- The analyzer walks the whole query even when `RejectUnbounded` is the only shape level set.
- `Strip` and `Analyze` are two passes over one tree, and could be one.
- `Counter` caches an accessor for every collection type it sees and never trims, so a type from a
  collectible assembly is held for the life of the process.
  [src/EfQueryComplexity/Counter.cs:6](src/EfQueryComplexity/Counter.cs#L6)

## Not a problem

### 11. Every compiled query holds its expression tree

`ValueChecker` keeps the query expression so it can print a message for a violation, which looked
like it pinned a tree for the life of a compiled query cache entry. Entity Framework pins the same
tree anyway: `CompiledQueryCacheKeyGenerator.CompiledQueryCacheKey` has an `Expression _query` field
and is the key the compiled query cache is keyed by, and
`RelationalCommandCache.CommandCacheKey` holds one as well. `QueryCompiler.ExecuteCore` passes one
expression instance to both the cache key and `CompileQueryCore`, so the field is another reference
to an object that is already held, not another tree.

The one case left is `EF.CompileQuery`, which does not use that cache, and there the tree is held for
as long as the caller holds the compiled query. That is small enough to leave alone.

## Fixed

- **1. An escalated value violation threw only on the first execution.** `logged` is now rolled back
  when `ComplexityLogger.Log` throws, so `ConfigureWarnings(Throw)` keeps throwing for every
  execution. Covered by `LoggingTests.ConfigureWarningsThrowsForEveryValueExecution`.
- **2. A per query override could not turn on a value check.** `QueryInterceptor` now throws for an
  override that sets `MaxTake` or `MaxInValues` when the configured levels set neither, rather than
  skipping the check silently. Covered by `OverrideTests.ValueOverrideNeedsConfiguredValueLevels`
  and `ShapeOverrideWithoutConfiguredShapeLevels`, and documented in the readme.
- **3. A sqlServerCostLimit of zero turned the governor off.** `UseQueryComplexity` now requires one
  greater than zero. Covered by `RegistrationTests.ZeroCostLimitThrows`, and documented in the
  readme.
- **4. A bare exception for the wrong provider.** `CostLimitInterceptor` throws
  `InvalidOperationException`. Not covered, since reaching it needs a second database provider
  referenced by the test project.
- **5. The message printed the query the markers were still in.** `QueryInterceptor` prints the
  stripped query, which is the one it measured. Covered by `OverrideTests.MessageExcludesMarkers`.
- **6. The message was built even when the event was ignored.** `ComplexityLogger.Log` takes a
  `Func<string>` and asks `ShouldLog` and `NeedsEventData` first, so a silenced or filtered event no
  longer prints the query expression.
- **7. Allocation on the per execution path.** `ValuePlan.Evaluate` is replaced by `LargestTake` and
  `LargestInValues`, read once per execution and compared against both sets of levels by
  `Violations.ForValues`, which returns null rather than an empty list.
- **8. Constants re-measured for every execution.** Folded into `takeConstant` and `inConstant` while
  the plan is built.
- **9. Reflection for every execution of a HashSet Contains.** `Counter` compiles a
  `Func<object, int>` for each type instead of caching a `PropertyInfo`.
- **10. A tree was rebuilt and dropped.** `MarkerReader.Read` reads the markers without rebuilding,
  and `ComplexityQueryCompiler` uses it. `ValueChecker` strips lazily when it builds a message, so a
  value message no longer prints the markers either. Covered by
  `OverrideTests.ValueMessageExcludesMarkers`.
