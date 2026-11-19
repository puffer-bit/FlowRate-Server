using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Database;
using Server.Database.PostgreSql;
using Server.Database.Sqlite;

namespace Server.Infrastructure.Database;

public static class DatabaseInitializer
{
    /// <summary>Prepares the database of the configured provider and applies its pending migrations.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ServerDbContext>>();
        await using var context = await factory.CreateDbContextAsync(ct);
        if (context.Database.IsSqlite())
            await SqliteDatabase.MigrateAsync(context, ct);
        else
            await PostgreSqlDatabase.MigrateAsync(context, ct);
    }
}
