using Microsoft.EntityFrameworkCore;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Import;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class SqliteImportValidationTests
{
    [Fact]
    public async Task SqliteTargetIsRejectedWithoutCreatingSourceOrTargetFiles()
    {
        var missing = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".db");
        await using var target = new ServerDbContext(new DbContextOptionsBuilder<ServerDbContext>().UseSqlite($"Data Source={missing}").Options);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SqlitePostgreSqlImporter().ImportAsync(missing, target));
        Assert.Contains("PostgreSQL", error.Message);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public async Task MissingSourceIsRejectedBeforeConnectingToTarget()
    {
        var missing = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".db");
        await using var target = new ServerDbContext(new DbContextOptionsBuilder<ServerDbContext>()
            .UseNpgsql("Host=invalid.example;Database=unreachable;Username=test").Options);
        await Assert.ThrowsAsync<FileNotFoundException>(() => new SqlitePostgreSqlImporter().ImportAsync(missing, target));
        Assert.False(File.Exists(missing));
    }
}
