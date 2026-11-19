using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Database.Sqlite;

/// <summary>
/// Defines <see cref="SqliteFunctions"/> on every connection before it is first opened, including connections
/// handed to the options from outside; a connection keeps them for every later opening.
/// </summary>
public sealed class SqliteFunctionsInterceptor : DbConnectionInterceptor
{
    private readonly ConditionalWeakTable<DbConnection, object> _registered = new();

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result)
    {
        Register(connection);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Register(connection);
        return ValueTask.FromResult(result);
    }

    private void Register(DbConnection connection)
    {
        if (_registered.TryAdd(connection, true))
            SqliteFunctions.Register(connection);
    }
}
