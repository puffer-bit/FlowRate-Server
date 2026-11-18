using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Infrastructure.Database;
using Server.Database.PostgreSql.Import;
using Server.Infrastructure.Options;

namespace Server.Asp.Commands;

public static class ImportSqliteCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine("Usage: Server.Asp import-sqlite --source <server.db> [--dry-run]");
            Console.WriteLine("Stop both servers. Configure the empty PostgreSQL target with Database__Provider and Database__ConnectionString.");
            Console.WriteLine("Dry run initializes the target schema, verifies a full import, then rolls back rows and sequences.");
            return 0;
        }
        string? source = null;
        var dryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--source" && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) && source == null) source = args[++i];
            else if (args[i] == "--dry-run" && !dryRun) dryRun = true;
            else
            {
                Console.Error.WriteLine("Invalid import arguments. Use import-sqlite --help.");
                return 2;
            }
        }
        if (string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine("--source <server.db> is required.");
            return 2;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var configuration = WebApplication.CreateBuilder(Array.Empty<string>()).Configuration;
            var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new();
            var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new();
            var options = new DbContextOptionsBuilder<ServerDbContext>();
            options.UseServerDatabase(database, storage);
            await using var target = new ServerDbContext(options.Options);
            var result = await new SqlitePostgreSqlImporter().ImportAsync(source, target, dryRun,
                table => Console.WriteLine($"Verified {table.Name}: {table.Rows} rows"), cancellation.Token);
            Console.WriteLine($"{(result.DryRun ? "Dry run verified and rolled back" : "Import committed")}: " +
                $"{result.Tables.Count} tables, {result.Tables.Sum(t => t.Rows)} rows. SQLite source unchanged.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Import cancelled. Check the target before retrying.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Import failed: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }
}
