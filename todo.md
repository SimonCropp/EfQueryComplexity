# Todo

Findings from a review on 2026-09-16. 1 to 9 are fixed. What is left keeps its original number.

## Performance

### 10. A tree is rebuilt and dropped

[src/EfQueryComplexity/ComplexityQueryCompiler.cs:77](src/EfQueryComplexity/ComplexityQueryCompiler.cs#L77)

`MarkerReader.Strip` is called for the ignore flag and the override, and the stripped tree is
discarded. A read only scan avoids rebuilding the spine of every query that carries a marker.

A value message therefore still prints the query with its markers in it, unlike a shape message. If
the stripped tree is kept rather than dropped, hand it to the `ValueChecker` and the two paths read
the same.

### 11. Every compiled query holds its expression tree

[src/EfQueryComplexity/ValueChecker.cs:8](src/EfQueryComplexity/ValueChecker.cs#L8)

The checker is captured by the delegate Entity Framework caches, so a compiled query with a value
plan keeps its whole expression tree, and anything constant in it such as an `EF.Constant`
collection, for as long as the cache entry lives. It is only needed to print a message for a
violation. Clearing the field after the first print would at least bound it.

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
  The value path still prints its markers, which is part of 10.
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
