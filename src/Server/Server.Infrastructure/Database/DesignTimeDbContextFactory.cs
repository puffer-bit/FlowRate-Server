using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Server.Infrastructure.Options;

namespace Server.Infrastructure.Database;

/// <summary>Used by `dotnet ef` so migrations can be created without starting the server.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
{
    public ServerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite(new StorageOptions().DatabaseConnectionString)
            .Options;

        return new ServerDbContext(options);
    }
}
