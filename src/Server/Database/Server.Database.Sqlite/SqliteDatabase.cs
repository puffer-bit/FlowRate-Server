using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Database.Sqlite;

/// <summary>The SQLite provider of <see cref="ServerDbContext"/>: its options, migrations and preparation.</summary>
public static class SqliteDatabase
{
    private const int ReadOnlyError = 8;
    private const int CantOpenError = 14;

    /// <summary>Uses SQLite with the migrations, functions and pragmas of this project.</summary>
    public static DbContextOptionsBuilder UseServerSqlite(this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlite(connectionString, Migrations).AddInterceptors(Interceptors);

    /// <inheritdoc cref="UseServerSqlite(DbContextOptionsBuilder, string)"/>
    public static DbContextOptionsBuilder UseServerSqlite(this DbContextOptionsBuilder builder, SqliteConnection connection) =>
        builder.UseSqlite(connection, Migrations).AddInterceptors(Interceptors);

    /// <inheritdoc cref="UseServerSqlite(DbContextOptionsBuilder, string)"/>
    public static DbContextOptionsBuilder<TContext> UseServerSqlite<TContext>(this DbContextOptionsBuilder<TContext> builder,
        string connectionString) where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)((DbContextOptionsBuilder)builder).UseServerSqlite(connectionString);

    /// <inheritdoc cref="UseServerSqlite(DbContextOptionsBuilder, string)"/>
    public static DbContextOptionsBuilder<TContext> UseServerSqlite<TContext>(this DbContextOptionsBuilder<TContext> builder,
        SqliteConnection connection) where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)((DbContextOptionsBuilder)builder).UseServerSqlite(connection);

    /// <summary>Returns why the connection string cannot be used, or null.</summary>
    public static string? ValidateConnectionString(string connectionString)
    {
        try
        {
            return string.IsNullOrWhiteSpace(new SqliteConnectionStringBuilder(connectionString).DataSource)
                ? "The SQLite connection string has no Data Source."
                : null;
        }
        catch (ArgumentException)
        {
            return "The connection string is invalid for SQLite.";
        }
    }

    /// <summary>Creates the directory of the file, switches to the write-ahead log and applies pending migrations.</summary>
    public static async Task MigrateAsync(ServerDbContext context, CancellationToken ct = default)
    {
        var dataSource = new SqliteConnectionStringBuilder(context.Database.GetConnectionString()).DataSource;
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

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

    private static readonly SqliteFunctionsInterceptor Functions = new();
    private static readonly SqliteSynchronousInterceptor Synchronous = new();
    private static readonly Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] Interceptors = [Functions, Synchronous];

    private static void Migrations(Microsoft.EntityFrameworkCore.Infrastructure.SqliteDbContextOptionsBuilder options) =>
        options.MigrationsAssembly(typeof(SqliteDatabase).Assembly.GetName().Name);
}
