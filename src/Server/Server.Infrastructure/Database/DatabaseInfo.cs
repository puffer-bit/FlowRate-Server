using Microsoft.Extensions.Options;
using Server.Application.Abstractions.Persistence;
using Server.Infrastructure.Options;

namespace Server.Infrastructure.Database;

public sealed class DatabaseInfo(IOptions<DatabaseOptions> options) : IDatabaseInfo
{
    public string Provider { get; } = options.Value.Provider switch
    {
        DatabaseProvider.Sqlite => "SQLite",
        DatabaseProvider.PostgreSql => "PostgreSQL",
        _ => throw new InvalidOperationException("Unsupported database provider.")
    };
}
