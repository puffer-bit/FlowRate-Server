using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Server.Infrastructure.Options;

namespace Server.Infrastructure.Database;

/// <summary>Uses the same database settings as the server without starting it.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
{
    public ServerDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new();
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new();
        var options = new DbContextOptionsBuilder<ServerDbContext>();
        options.UseServerDatabase(database, storage);
        return new ServerDbContext(options.Options);
    }
}
