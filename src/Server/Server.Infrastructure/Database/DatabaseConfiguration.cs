using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure.Options;

namespace Server.Infrastructure.Database;

public static class DatabaseConfiguration
{
    public const string PostgreSqlMigrationsAssembly = "Server.Migrations.PostgreSql";

    public static DbContextOptionsBuilder UseServerDatabase(this DbContextOptionsBuilder builder,
        DatabaseOptions database, StorageOptions storage)
    {
        Validator.ValidateObject(database, new ValidationContext(database), validateAllProperties: true);
        var connectionString = database.GetConnectionString(storage);
        return database.Provider switch
        {
            DatabaseProvider.Sqlite => builder.UseSqlite(connectionString)
                .AddInterceptors(new SqliteSynchronousInterceptor()),
            DatabaseProvider.PostgreSql => builder.UseNpgsql(connectionString,
                options => options.MigrationsAssembly(PostgreSqlMigrationsAssembly)),
            _ => throw new InvalidOperationException("Unsupported database provider.")
        };
    }
}
