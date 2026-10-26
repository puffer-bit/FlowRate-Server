using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Server.Infrastructure.Options;

namespace Server.Infrastructure.Database;

public static class DatabaseInitializer
{
    private const int ReadOnlyError = 8;
    private const int CantOpenError = 14;

    /// <summary>Creates the database directory if needed and applies pending migrations.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var options = services.GetRequiredService<IOptions<StorageOptions>>().Value;

        var dataSource = new SqliteConnectionStringBuilder(options.DatabaseConnectionString).DataSource;
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var factory = services.GetRequiredService<IDbContextFactory<ServerDbContext>>();
        await using var context = await factory.CreateDbContextAsync(ct);
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", ct);
            await context.Database.MigrateAsync(ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is ReadOnlyError or CantOpenError)
        {
            throw new InvalidOperationException(
                $"Database '{Path.GetFullPath(dataSource)}' cannot be written by the server process " +
                $"(user '{Environment.UserName}'). Make the file and its directory '{directory}' writable for this user.",
                ex);
        }
    }
}
