/// <summary>
/// Applies the SQL Server query governor cost limit to every connection.
/// </summary>
/// <remarks>
/// SQL Server then refuses any statement whose estimated plan cost is greater than the limit, with
/// error 8649. The cost is the optimizer's estimate in its own units rather than a time, so it is a
/// guard rather than a guarantee.
/// </remarks>
sealed class CostLimitInterceptor :
    DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        var limit = GetLimit(connection, eventData);
        if (limit == null)
        {
            return;
        }

        using var command = CreateCommand(connection, limit.Value);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, Cancel cancel = default)
    {
        var limit = GetLimit(connection, eventData);
        if (limit == null)
        {
            return;
        }

        await using var command = CreateCommand(connection, limit.Value);
        await command.ExecuteNonQueryAsync(cancel);
    }

    static int? GetLimit(DbConnection connection, ConnectionEndEventData eventData)
    {
        var limit = eventData.Context?
            .GetService<IDbContextOptions>()
            .FindExtension<QueryComplexityOptionsExtension>()?
            .SqlServerCostLimit;
        if (limit == null)
        {
            return null;
        }

        // Checked by name to avoid a dependency on the SQL Server provider
        var type = connection.GetType().FullName;
        if (type != "Microsoft.Data.SqlClient.SqlConnection")
        {
            throw new($"sqlServerCostLimit is SQL Server only, but the connection is {type}.");
        }

        return limit;
    }

    // Applied on every open, since a pooled connection is reset to its login defaults when it is
    // reused
    static DbCommand CreateCommand(DbConnection connection, int limit)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"SET QUERY_GOVERNOR_COST_LIMIT {limit}";
        return command;
    }
}
