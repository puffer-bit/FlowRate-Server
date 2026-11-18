using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Server.Database.Sqlite;

/// <summary>Lets `dotnet ef` create SQLite migrations in this project without starting the server.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
{
    public ServerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ServerDbContext>();
        options.UseServerSqlite("Data Source=design-time.db");
        return new ServerDbContext(options.Options);
    }
}
