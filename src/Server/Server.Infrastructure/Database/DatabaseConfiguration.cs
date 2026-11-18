using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Database.PostgreSql;
using Server.Database.Sqlite;
using Server.Infrastructure.Options;

namespace Server.Infrastructure.Database;

/// <summary>Picks the database provider of <see cref="ServerDbContext"/> from the "Database" section.</summary>
public static class DatabaseConfiguration
{
    public static DbContextOptionsBuilder UseServerDatabase(this DbContextOptionsBuilder builder,
        DatabaseOptions database, StorageOptions storage)
    {
        Validator.ValidateObject(database, new ValidationContext(database), validateAllProperties: true);
        var connectionString = database.GetConnectionString(storage);
        return database.Provider switch
        {
            DatabaseProvider.Sqlite => builder.UseServerSqlite(connectionString),
            DatabaseProvider.PostgreSql => builder.UseServerPostgreSql(connectionString),
            _ => throw new InvalidOperationException("Unsupported database provider.")
        };
    }
}
