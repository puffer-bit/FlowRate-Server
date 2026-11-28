using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Server.Database.PostgreSql;

/// <summary>
/// Lets `dotnet ef` create PostgreSQL migrations in this project without starting the server; creating a migration
/// does not connect, so the connection string only names a host.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
{
    public ServerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ServerDbContext>();
        options.UseServerPostgreSql("Host=localhost");
        return new ServerDbContext(options.Options);
    }
}
