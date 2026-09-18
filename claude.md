# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build
dotnet build src --configuration Release

# Run tests
dotnet test --solution src/EfQueryComplexity.slnx --configuration Release --no-build --no-restore

# Run one test class or method. TUnit uses tree node filters, so the VSTest
# "--filter FullyQualifiedName~" form matches nothing, and --nologo reports zero tests
dotnet test --project src/Tests --configuration Release --no-build -- --treenode-filter "/*/*/ShapeTests/*"

# Or run the test executable directly
src/Tests/bin/Release/net10.0/Tests.exe --treenode-filter "/*/*/ShapeTests/NodeLevel"
```

Tests are TUnit on Microsoft.Testing.Platform. `global.json` selects that runner, which is what makes `dotnet test --solution` work. Test projects must not reference `Microsoft.NET.Test.Sdk`, and need `OutputType` of `Exe`.

## Project Overview

EfQueryComplexity measures Entity Framework Core queries and, per check, logs a query that exceeds a log level and throws for one that exceeds a throw level.

**Package**: [EfQueryComplexity on NuGet](https://www.nuget.org/packages/EfQueryComplexity/)

## Architecture

Levels come from `UseQueryComplexity(logAt, throwAt, sqlServerCostLimit)` and live in an options extension. All three are part of `GetServiceProviderHashCode` and `ShouldUseSameServiceProvider`, so contexts with different levels never share a compiled query cache and so never reuse a query that was checked under different levels.

The service provider alone is not enough. Entity Framework keys a compiled query on the model it was compiled for, and caches the models and the compiled queries in an `IMemoryCache` that `UseMemoryCache` can hand to several providers. So the log and throw levels are also part of the model cache key, through `ComplexityModelCacheKeyFactory`. `Validate` rejects a context whose `IModelCacheKeyFactory` is not that one.

| File | Purpose |
| --- | --- |
| `QueryComplexityExtensions.cs` | Entry point: `UseQueryComplexity`, `IgnoreQueryComplexity`, `WithQueryComplexity` |
| `QueryComplexityLimits.cs` | The levels, and `LogDefaults` |
| `QueryComplexityOptionsExtension.cs` | Holds the levels, and keys the internal service provider |
| `QueryInterceptor.cs` | `IQueryExpressionInterceptor`: measures shape and strips markers, once per compiled shape |
| `ShapeAnalyzer.cs` | One pass measuring nodes, depth, operators, navigations and includes |
| `UnboundedDetector.cs` | The types of the rows a query can return without a limit, found once per compiled shape |
| `UnboundedEntities.cs` | Which of those types `RejectUnbounded` checks: `All`, `None`, `AllExcept`, `Only` |
| `Sequences.cs` | Whether a type is a sequence, and what it holds |
| `Markers.cs`, `MarkerReader.cs` | The per query marker calls, and reading (`Read`) and removing (`Strip`) them |
| `ComplexityModelCacheKeyFactory.cs` | Puts the levels in the model cache key, so different levels never share a model, and so never share a compiled query |
| `ComplexityQueryCompiler.cs` | Wraps the cached delegate so values are checked for every execution, and caches a query that throws |
| `ValuePlan.cs`, `ValueChecker.cs`, `Counter.cs` | Where Take counts and Contains lists come from, and checking them |
| `Violations.cs`, `ComplexityLogger.cs` | Comparing against levels, message text, and logging through EF |
| `CostLimitInterceptor.cs` | `SET QUERY_GOVERNOR_COST_LIMIT` on every connection open |

### Two places, because of when values exist

- **Shape** is measured in `QueryCompilationStarting`, which runs only when a shape is compiled. Values are not visible there: Entity Framework has already replaced a `Take` count or a `Contains` list with a parameter, and a different value does not recompile.
- **Values** are checked in `ComplexityQueryCompiler.CompileQueryCore`, which returns the delegate EF caches and runs for every execution. That is internal API (EF1001), suppressed in that one file, and it is only registered when a value level is set or `throwAt` is passed. Whether values are checked is decided before any query exists, so a per query override cannot turn the value checks on, and `QueryInterceptor` throws for one that tries.
- **A query that throws** does so while Entity Framework compiles it, and Entity Framework does not cache a query that fails to compile. `ComplexityQueryCompiler.CompileQueryCore` catches the `QueryComplexityException` and returns a delegate that throws a new one with the same message and violations. Entity Framework caches that like any compiled query, so a failing shape is measured and printed once.
- `ValueChecker.Check` is the only code that runs for every execution, so it reads each value once for both sets of levels and allocates nothing until something is violated.

### Markers

`IgnoreQueryComplexity()` and `WithQueryComplexity()` put a call to a method on `Markers` into the query. The override argument is `[NotParameterized]`, which keeps it a constant so it can be read while the query is compiled, and makes it part of the compiled query cache key. `QueryInterceptor` removes the calls with `MarkerReader.Strip`, without which Entity Framework cannot translate the query. `ComplexityQueryCompiler` runs first and only needs what they asked for, so it uses `MarkerReader.Read`, which rebuilds nothing.

## Testing conventions

- **TUnit** with **Verify** for snapshots. `*.verified.*` files are the committed expectations, `*.received.*` are gitignored actuals. Accept an intended change by moving the received file over the verified one.
- **Most tests need no database.** `ContextBuilder.Build` points at an unusable connection string and the tests call `ToQueryString()`, which compiles the query and runs the cached delegate, so both shape and value checks fire without connecting. It also disables service provider caching, so each test gets a fresh compiled query cache and "logged once" assertions stay deterministic.
- **Database tests** (`CostLimitTests`, `ExecutionTests`) use **EfLocalDb**, so LocalDB must be installed, and are capped with `[ParallelLimiter<DatabaseParallelLimit>]`.
- **The `SqlInstance` is built in `AssemblySetup`, in a `[Before(HookType.Assembly)]` hook, never in a `[ModuleInitializer]`.** Microsoft.Testing.Platform can start the test exe twice, and both processes would build the same template at once.
- Levels in tests start from `Limits.None`, so only the check under test is on.

## Benchmarks

`src/Benchmarks` is BenchmarkDotNet, and has to run as a Release build. `dotnet test` skips it. Run it from its own directory, so results land in its `BenchmarkDotNet.Artifacts`, which is gitignored:

```bash
cd src/Benchmarks
dotnet run --configuration Release -- --filter "*CounterBenchmarks*"
```

On a CPU with efficiency cores, add `--affinity` with a mask of the performance cores, such as `--affinity 15` for the first four. Otherwise a run can move to a slower core partway through, and identical code measures several times slower.

- ProjectDefaults signs it with `key.snk`, like `Tests`, and `InternalsVisibleTo` names it, so a benchmark can call an internal type such as `Counter`.
- Its own `Directory.Build.props` sets `IsPackageProject` to false. ProjectDefaults reads that before the project file and packs every Release project where it is not false, and the publish workflow pushes everything in `nugets`.

## Docs are generated

`readme.md` contains `snippet:` regions filled in by **MarkdownSnippets** when `Tests` builds, sourced from `#region` blocks in `src/Tests/Snippets.cs`. Never hand-edit inside a generated snippet block; change the snippet source and rebuild. Snippet lines wrap at 80 characters.

## CI

GitHub Actions workflows:

- `.github/workflows/test.yml` builds and tests on every push to main and every PR. Windows only, because the database tests need LocalDB, which it starts explicitly so a missing LocalDB fails as a clear error rather than a timeout. Received snapshots are uploaded as an artifact when a test fails.
- `.github/workflows/publish-nuget.yml` runs on any tag push. It builds, packs, tests, then pushes to nuget.org with Trusted Publishing (OIDC), so no API key is stored. It needs a one-time trusted publishing policy on nuget.org, scoped to this repo and that workflow file. The version comes from `Version` in `src/Directory.Build.props`, not from the tag, so bump it and tag that commit.
- `.github/workflows/merge-dependabot.yml` follows GitHub's [Dependabot auto-merge tutorial](https://docs.github.com/en/code-security/tutorials/secure-your-dependencies/automate-dependabot-with-actions), and turns on auto-merge (squash) for patch and minor updates. That relies on two repository settings: "Allow auto-merge", and the "Require tests on main" ruleset, which requires the `test` check so auto-merge waits for it.
- **The ruleset blocks pushes to main by the Actions token.** Repository admins can bypass it, so pushing to main directly still works, but GitHub does not allow the Actions app as a bypass actor on a personal repository. `on-push-do-docs.yml` therefore fails, rather than silently dropping its commit, if it ever has docs changes to push to main.

## Code conventions

- Public types live in the `EfQueryComplexity` namespace. Internal types have no namespace and live in the global namespace.
- Lambda parameters are named `_`, including where used. Nested lambdas name the outer one.
- Line comments go on their own line above the code, never trailing.
- Warnings are errors, and code style is enforced during the build. Every public member needs XML docs.
