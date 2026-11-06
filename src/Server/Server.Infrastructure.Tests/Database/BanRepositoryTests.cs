using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Moderation;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Repositories;
using Server.Infrastructure.Mappers;
using Shared.Enums.User;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class BanRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bans-{Guid.NewGuid():N}.db");
    private readonly Factory _factory;
    public BanRepositoryTests() => _factory = new Factory(new DbContextOptionsBuilder<ServerDbContext>()
        .UseSqlite($"Data Source={_path}").Options);
    public void Dispose() { SqliteConnection.ClearAllPools(); File.Delete(_path); }

    [Fact]
    public async Task Migration_ImportsLegacyAccountAndIpBans_AndClearsTheOldIpList()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("PinnedChatMessages");
            var model = CoordinatorInstanceModel.Create("legacy", "0.0.0.0", 1234, DateTime.UtcNow);
            model.SetAccessList(new AccessListSettings([], ["::ffff:192.0.2.1", "192.0.2.1", "2001:0db8::1"]));
            db.CoordinatorInstances.Add(model.ToEntity());
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO UserEntities (IdentityHash, Username, CoordinatorInstanceId, State, LastStatus,
                    FirstConnectionTime, LastConnectionTime)
                VALUES (X'00', 'legacy', {0}, 1, 0, '2026-01-01', '2026-01-01');
                """, model.Id);
            await migrator.MigrateAsync();
            var entries = await new BanRepository(_factory).GetAsync(model.Id);
            Assert.Equal(3, entries.Count);
            Assert.Contains(entries, b => b.Kind == BanKind.Account && b.UserId != null);
            Assert.Contains(entries, b => b.Kind == BanKind.Ip && b.Address == "192.0.2.1");
            Assert.Contains(entries, b => b.Kind == BanKind.Ip && b.Address == "2001:db8::1");
            Assert.All(entries, b => Assert.Contains("reason was not recorded", b.Reason));
            db.ChangeTracker.Clear();
            Assert.Empty((await db.CoordinatorInstances.SingleAsync()).BannedIPs);
        }
    }

    [Fact]
    public async Task Audit_SurvivesReload_AndRevokeIsScopedAndIdempotent()
    {
        await using (var db = _factory.CreateDbContext()) await db.Database.MigrateAsync();
        var repository = new BanRepository(_factory);
        var now = DateTime.UtcNow;
        var ban = new GlobalBan(Guid.NewGuid(), "one", BanKind.Ip, 12, "192.0.2.1", "Abuse", 5, now,
            now.AddDays(1), UserState.Suspended);
        await repository.AddAsync(ban);
        Assert.Equal(ban, Assert.Single(await new BanRepository(_factory).GetAsync("one")));
        await repository.RevokeAsync("other", ban.Id, 8, now);
        Assert.Null(Assert.Single(await repository.GetAsync("one")).RevokedAt);
        await repository.RevokeAsync("one", ban.Id, 8, now);
        await repository.RevokeAsync("one", ban.Id, 9, now.AddHours(1));
        var revoked = Assert.Single(await repository.GetAsync("one"));
        Assert.Equal(8, revoked.RevokedBy);
        Assert.Equal(now, revoked.RevokedAt);
        Assert.Equal("Abuse", revoked.Reason);
        Assert.Empty(await repository.GetAsync("other"));
    }

    private sealed class Factory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }
}
