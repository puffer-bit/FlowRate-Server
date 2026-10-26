using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Infrastructure.Database;

/// <summary>
/// Makes every connection commit without waiting for the disk on each transaction. With the write-ahead log
/// the database stays consistent; only the last transactions can be lost when the machine loses power.
/// </summary>
public sealed class SqliteSynchronousInterceptor : DbConnectionInterceptor
{
    private const string Pragma = "PRAGMA synchronous = NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragma;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragma;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
