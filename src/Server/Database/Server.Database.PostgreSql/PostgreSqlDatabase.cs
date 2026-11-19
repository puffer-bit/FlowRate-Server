using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Server.Database.PostgreSql;

/// <summary>The PostgreSQL provider of <see cref="ServerDbContext"/>: its options, migrations and preparation.</summary>
public static class PostgreSqlDatabase
{
    private static readonly PostgreSqlTimestampInterceptor Timestamps = new();

    /// <summary>Uses PostgreSQL with the migrations of this project.</summary>
    public static DbContextOptionsBuilder UseServerPostgreSql(this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString,
                options => options.MigrationsAssembly(typeof(PostgreSqlDatabase).Assembly.GetName().Name))
            .AddInterceptors(Timestamps);

    /// <inheritdoc cref="UseServerPostgreSql(DbContextOptionsBuilder, string)"/>
    public static DbContextOptionsBuilder<TContext> UseServerPostgreSql<TContext>(this DbContextOptionsBuilder<TContext> builder,
        string connectionString) where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)((DbContextOptionsBuilder)builder).UseServerPostgreSql(connectionString);

    /// <summary>Returns why the connection string cannot be used, or null.</summary>
    public static string? ValidateConnectionString(string connectionString)
    {
        try
        {
            return string.IsNullOrWhiteSpace(new NpgsqlConnectionStringBuilder(connectionString).Host)
                ? "The PostgreSQL connection string has no Host."
                : null;
        }
        catch (ArgumentException)
        {
            return "The connection string is invalid for PostgreSQL.";
        }
    }

    /// <summary>Applies pending migrations.</summary>
    public static Task MigrateAsync(ServerDbContext context, CancellationToken ct = default) =>
        context.Database.MigrateAsync(ct);
}
