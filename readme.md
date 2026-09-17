# <img src="/src/icon.png" height="30px"> EfQueryComplexity

[![Build status](https://github.com/SimonCropp/EfQueryComplexity/actions/workflows/test.yml/badge.svg?branch=main)](https://github.com/SimonCropp/EfQueryComplexity/actions/workflows/test.yml)
[![NuGet Status](https://img.shields.io/nuget/v/EfQueryComplexity.svg)](https://www.nuget.org/packages/EfQueryComplexity/)

**See [Milestones](../../milestones?state=closed) for release notes.**

Detects overly complex Entity Framework Core queries and either logs them or throws. Each check has two levels: the level a query is logged at, which has defaults, and the level a query throws at, which is opt in.

Entity Framework has no limits of its own on the size or shape of a query, and the team [closed the request for unbounded result set warnings as not planned](https://github.com/dotnet/efcore/issues/5089). Limits like these usually sit in front of Entity Framework, in an OData or GraphQL layer, and so only cover queries that arrive that way.


## Untrusted clients

An API that lets the client shape the query, such as GraphQL or OData, hands part of the query to whoever sends the request. One small request can ask for every row of a table, nest navigations many levels deep, or send a `Contains` list with thousands of values. Each costs the database and the server far more than it costs the client to send, so a handful of them, repeated, is enough to make the API slow or unavailable. This is a denial of service by resource exhaustion, and it needs no bug in the API to work, only a query the API did not expect.

The checks bound what one query can ask for, whichever layer built it:

| Attack | Check |
| --- | --- |
| Requesting every row | `RejectUnbounded`, `MaxTake` |
| Deeply nested or very large queries | `MaxNodes`, `MaxDepth`, `MaxOperators` |
| Long navigation chains and includes, which multiply joins | `MaxNavigationDepth`, `MaxIncludes`, `MaxIncludeDepth` |
| Huge `IN` lists | `MaxInValues` |
| A query that passes every check but is still expensive | [SQL Server cost limit](#sql-server-cost-limit) |

Only throw levels stop a query. Log levels report it and let it run. A practical rollout is to log first, see which levels real clients reach, then set throw levels above that.

These checks limit the cost of each query. They do not replace limits on how often a client can send one, so still use authentication, rate limiting, and request and command timeouts.

Background:

- [OWASP API Security Top 10: API4:2023 Unrestricted Resource Consumption](https://api-security.owasp.org/editions/2023/en/0xa4-unrestricted-resource-consumption)
- [OWASP GraphQL Cheat Sheet: DoS Prevention](https://cheatsheetseries.owasp.org/cheatsheets/GraphQL_Cheat_Sheet.html#dos-prevention), which covers query depth, amount and cost limits
- [CWE-770: Allocation of Resources Without Limits or Throttling](https://cwe.mitre.org/data/definitions/770.html)


## NuGet package

https://nuget.org/packages/EfQueryComplexity/


## Features

- **Two levels per check**: a query is logged at one level and throws at another
- **Measured once**: shape is measured while a query is compiled, so each distinct query costs it once
- **Values checked every execution**: `Take` counts and `Contains` lists only exist while a query runs
- **Per query overrides**: raise a level for one query, or skip every check for it
- **Standard logging**: warnings go through the Entity Framework pipeline, so `LogTo`, `ILoggerFactory` and `ConfigureWarnings` all work
- **SQL Server cost limit**: hand the decision to SQL Server's own query governor


## Usage


### 1. Enable the checks

<!-- snippet: EnableQueryComplexity -->
<a id='snippet-EnableQueryComplexity'></a>
```cs
protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
    builder.UseQueryComplexity();
```
<sup><a href='/src/Tests/Snippets.cs#L9-L14' title='Snippet source file'>snippet source</a> | <a href='#snippet-EnableQueryComplexity' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With no levels passed, a query is logged when it exceeds `QueryComplexityLimits.LogDefaults`, and no query throws.


### 2. Choose the levels to log at

<!-- snippet: LogAt -->
<a id='snippet-LogAt'></a>
```cs
protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
    builder.UseQueryComplexity(
        logAt: QueryComplexityLimits.LogDefaults with
        {
            MaxTake = 500,
            RejectUnbounded = false
        });
```
<sup><a href='/src/Tests/Snippets.cs#L20-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-LogAt' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### 3. Choose the levels to throw at

Throwing is opt in, and every level has to be given, so nothing is enforced by accident:

<!-- snippet: ThrowAt -->
<a id='snippet-ThrowAt'></a>
```cs
protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
    builder.UseQueryComplexity(
        throwAt: new(
            MaxNodes: 4096,
            MaxDepth: 64,
            MaxOperators: 64,
            MaxNavigationDepth: 4,
            MaxIncludes: 16,
            MaxIncludeDepth: 4,
            MaxTake: 1000,
            MaxInValues: 1000,
            RejectUnbounded: true));
```
<sup><a href='/src/Tests/Snippets.cs#L36-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-ThrowAt' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A query that exceeds a throw level throws `QueryComplexityException`, which carries every level it exceeded in `Violations`.

A query is checked against the throw levels before the log levels, so a throw level below its log level would leave that log level unreachable. That is rejected as the context is constructed, rather than quietly logging nothing. Passing the same levels for both is allowed, and is how to say "only throw".


## Checks

| Level | Counts | Measured | Log default |
| --- | --- | --- | --- |
| `MaxNodes` | Expression nodes in the query | While compiled | 1000 |
| `MaxDepth` | Nesting depth of the query expression | While compiled | 50 |
| `MaxOperators` | LINQ operators, including in subqueries | While compiled | 30 |
| `MaxNavigationDepth` | Navigations in one member access chain | While compiled | 3 |
| `MaxIncludes` | `Include` calls | While compiled | 6 |
| `MaxIncludeDepth` | Navigations in one `Include` chain | While compiled | 3 |
| `MaxTake` | The value passed to `Take` | Every execution | 1000 |
| `MaxInValues` | Values in a `Contains` list | Every execution | 1000 |
| `RejectUnbounded` | A query returning rows with no `Take` | While compiled | `All` |

A check fires when the measured value is greater than the level. A level of `null` turns that check off.


### Unbounded queries

A query is bounded when it cannot return more rows than a `Take` allows:

- A query that returns one row, an aggregate or a count is bounded, so `First`, `Single`, `Count`, `Any`, `Sum` and friends never fire.
- `Take` bounds everything below it.
- `SelectMany`, `Join`, `GroupJoin`, `LeftJoin`, `RightJoin` and `Zip` return more rows than their source, so a `Take` below one of them bounds the source rather than the query.
- `Concat` and `Union` are bounded only when both sides are.
- Every other operator returns no more rows than its source.

The message names the types of the rows returned without a `Take`, and so does `QueryComplexityViolation.RowTypes`. A row type is the entity a query reads, not what it projects to, so `Employees.Select(_ => _.Name)` returns `Employee` rows. A query that joins in another sequence returns rows of both types: `Departments.SelectMany(_ => _.Employees)` returns `Department` and `Employee` rows.


### Choosing the types to check

Most apps know which tables stay small and which grow. `RejectUnbounded` takes an `UnboundedEntities`, so the check can cover only the types where returning every row is a problem. `true` converts to `UnboundedEntities.All` and `false` to `UnboundedEntities.None`.

Check every type except the ones known to have few rows:

<!-- snippet: RejectUnboundedAllExcept -->
<a id='snippet-RejectUnboundedAllExcept'></a>
```cs
protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
    builder.UseQueryComplexity(
        logAt: QueryComplexityLimits.LogDefaults with
        {
            // Few rows, so returning all of them is fine
            RejectUnbounded = UnboundedEntities.AllExcept(
                typeof(User),
                typeof(AccessGroup))
        });
```
<sup><a href='/src/Tests/Snippets.cs#L57-L69' title='Snippet source file'>snippet source</a> | <a href='#snippet-RejectUnboundedAllExcept' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or check only the types known to have many rows:

<!-- snippet: RejectUnboundedOnly -->
<a id='snippet-RejectUnboundedOnly'></a>
```cs
protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
    builder.UseQueryComplexity(
        logAt: QueryComplexityLimits.LogDefaults with
        {
            // Many rows, so every query for them needs a Take
            RejectUnbounded = UnboundedEntities.Only(
                typeof(Commitment))
        });
```
<sup><a href='/src/Tests/Snippets.cs#L75-L86' title='Snippet source file'>snippet source</a> | <a href='#snippet-RejectUnboundedOnly' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- A query fires when it returns rows of any checked type. A query that joins in a type that is not listed still fires for that type, so both types of a `SelectMany` have to be skipped for it to pass.
- Naming a type also names the types derived from it, and naming an interface names the types that implement it, so a marker interface can list many types at once.
- A query for a base class returns rows of its derived types too. It is checked by `Only` when a derived type is named, and only skipped by `AllExcept` when the base class is.
- Rows that are not entities, such as `SqlQuery<int>` or a list of values, are checked by `All` and `AllExcept`, and not by `Only`.
- Only the rows of the query itself count. A collection loaded by `Include`, or by a projection, is not checked, so with `AllExcept(typeof(User))`, `Users.Include(_ => _.Commitments)` does not fire.


### Take and IN list sizes

These values only exist while a query runs, so they are checked for every execution rather than once per query. That check needs Entity Framework's internal query compiler, which is registered when `MaxTake` or `MaxInValues` is set at either level. It is also registered whenever `throwAt` is passed, since it caches a query that throws, so the query is not measured again for every execution. Consequences:

- The package uses an internal API (EF1001), so it is tied to the Entity Framework major version it was built for.
- It conflicts with any other library that replaces `IQueryCompiler`, since the last one registered wins.
- A per query override cannot turn these checks on, since whether to check values is decided before any query exists. `WithQueryComplexity` that sets `MaxTake` or `MaxInValues`, for a context where neither is set, throws rather than leaving the query unchecked.

A value that is over a log level is logged the first time a compiled query exceeds it, rather than on every execution. A value over a throw level throws every time.


## Per query overrides

Skip every check for one query:

<!-- snippet: IgnoreQueryComplexity -->
<a id='snippet-IgnoreQueryComplexity'></a>
```cs
// Skips every check for this query
var employees = await context.Employees
    .IgnoreQueryComplexity()
    .ToListAsync();
```
<sup><a href='/src/Tests/Snippets.cs#L138-L145' title='Snippet source file'>snippet source</a> | <a href='#snippet-IgnoreQueryComplexity' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or replace levels for one query:

<!-- snippet: WithQueryComplexity -->
<a id='snippet-WithQueryComplexity'></a>
```cs
// Replaces levels for this query only
var employees = await context.Employees
    .WithQueryComplexity(
        new()
        {
            MaxTake = 5000
        })
    .Take(5000)
    .ToListAsync();
```
<sup><a href='/src/Tests/Snippets.cs#L152-L164' title='Snippet source file'>snippet source</a> | <a href='#snippet-WithQueryComplexity' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Every level left null keeps the configured value, and a level that is set replaces both the log and the throw level for that check. An override never starts throwing for a context that was not given throw levels. To turn one check off for a query use `int.MaxValue`. `RejectUnbounded` is a `bool` for a query: `true` checks every type and `false` none, whichever types were configured.

`MaxTake` and `MaxInValues` can only be changed for a query when the configured levels set one of them, since the value checks are otherwise not set up at all. An override that sets one anyway throws.

Each distinct set of levels is a constant in the query, so a query using them is compiled and checked separately.


## Logging

Warnings are logged as `QueryComplexityEventId.LimitExceeded` through the Entity Framework pipeline, so they reach `LogTo`, an `ILoggerFactory`, and a `DiagnosticSource`. That also means the usual configuration applies, including turning the warning into an error:

<!-- snippet: EscalateWithConfigureWarnings -->
<a id='snippet-EscalateWithConfigureWarnings'></a>
```cs
protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
    builder
        .UseQueryComplexity()
        .ConfigureWarnings(
            _ => _.Throw(QueryComplexityEventId.LimitExceeded));
```
<sup><a href='/src/Tests/Snippets.cs#L105-L113' title='Snippet source file'>snippet source</a> | <a href='#snippet-EscalateWithConfigureWarnings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Use `Ignore` instead of `Throw` to silence it.

The same configuration can turn the warning into an error without mentioning it. A context whose warnings all throw, such as one built by [EfLocalDb](https://github.com/SimonCropp/LocalDb), which uses `Default(WarningBehavior.Throw)`, throws `InvalidOperationException` for any query over a log level, and the message starts with "An error was generated for warning 'EfQueryComplexity.LimitExceeded'". A behavior set for one event takes precedence over the default, so to keep only logging:

<!-- snippet: KeepLoggingWhenWarningsThrow -->
<a id='snippet-KeepLoggingWhenWarningsThrow'></a>
```cs
protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
    builder
        .UseQueryComplexity()
        .ConfigureWarnings(
            _ => _
                .Default(WarningBehavior.Throw)
                .Log(QueryComplexityEventId.LimitExceeded));
```
<sup><a href='/src/Tests/Snippets.cs#L119-L129' title='Snippet source file'>snippet source</a> | <a href='#snippet-KeepLoggingWhenWarningsThrow' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The message names every level that was exceeded and then prints the query, bounded to 1000 characters. The query that broke a level is the one that prints long, and without a bound every log line reporting it would carry the whole expression tree.


## SQL Server cost limit

The levels above bound the shape of a query, not what it costs to run. An allowed query over a large unindexed table is still expensive. SQL Server can make that call itself:

<!-- snippet: SqlServerCostLimit -->
<a id='snippet-SqlServerCostLimit'></a>
```cs
protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
    builder
        .UseSqlServer("connection-string")
        .UseQueryComplexity(sqlServerCostLimit: 300);
```
<sup><a href='/src/Tests/Snippets.cs#L92-L99' title='Snippet source file'>snippet source</a> | <a href='#snippet-SqlServerCostLimit' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`SET QUERY_GOVERNOR_COST_LIMIT` is applied to every connection as it opens, and SQL Server then refuses any statement whose estimated plan cost is greater than the limit, with error 8649.

- The cost is the optimizer's estimate in its own units, not a time, so treat it as relative. It is an estimate, so stale statistics can still let a slow query through.
- It has to be greater than zero. SQL Server reads a cost limit of zero as the query governor being off, so passing zero throws rather than silently allowing everything.
- It is applied on every open, because reusing a pooled connection resets session state.
- It is a property of the connection, so `IgnoreQueryComplexity()` does not lift it for one query.
- SQL Server only, including Azure SQL. Other providers throw.


## How it works

- **Shape** is measured by an `IQueryExpressionInterceptor`, which runs only when a query shape is compiled. Each distinct query is measured once, and logged once. A query that throws does so every time it is used, but the failure is cached in place of the compiled query, so it is not measured again.
- **Values** are checked by a wrapper around the delegate Entity Framework caches for a query, so every execution is checked, including executions of compiled queries.
- **Levels are part of the key for Entity Framework's internal service provider**, so contexts with different levels never share a compiled query. Use a few fixed configurations rather than varying levels per request, or Entity Framework's `ManyServiceProvidersCreatedWarning` fires.


## Limitations

- Raw SQL is not analysed. `FromSql`, `ExecuteSql` and `SqlQuery` pass through, and only LINQ composed on top of them is measured.
- Navigation depth is measured per member access chain, not across separate lambdas.
- Using `IgnoreQueryComplexity()` or `WithQueryComplexity()` without calling `UseQueryComplexity()` gives Entity Framework's "could not be translated" error, since nothing removes the marker.


## Icon

[Pattern](https://thenounproject.com/icon/pattern-8340056/) from [The Noun Project](https://thenounproject.com)
