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
    public async Task Ban_SurvivesReload_AndDeletionIsScopedAndIdempotent()
    {
        await using (var db = _factory.CreateDbContext()) await db.Database.MigrateAsync();
        var repository = new BanRepository(_factory);
        var now = DateTime.UtcNow;
        var ban = new GlobalBan(Guid.NewGuid(), "one", BanKind.Ip, 12, "192.0.2.1", "Abuse", 5, now,
            now.AddDays(1), UserState.Suspended);
        await repository.AddAsync(ban);
        Assert.Equal(ban, Assert.Single(await new BanRepository(_factory).GetAsync("one")));
        await repository.DeleteAsync("other", ban.Id);
        Assert.Equal(ban, Assert.Single(await repository.GetAsync("one")));
        await repository.DeleteAsync("one", ban.Id);
        await repository.DeleteAsync("one", ban.Id);
        Assert.Empty(await repository.GetAsync("one"));
        Assert.Empty(await repository.GetAsync("other"));
    }

    [Fact]
    public async Task HistoryRemovalMigration_DeletesCompletedBans_AndRestoresOnlyUnbannedAccounts()
    {
        await using var db = _factory.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("GlobalBans");
        var coordinator = CoordinatorInstanceModel.Create("history", "0.0.0.0", 1234, DateTime.UtcNow);
        db.CoordinatorInstances.Add(coordinator.ToEntity());
        await db.SaveChangesAsync();
        foreach (var id in new[] { 101, 102, 103 })
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO UserEntities (Id, IdentityHash, Username, CoordinatorInstanceId, State, LastStatus,
                    FirstConnectionTime, LastConnectionTime)
                VALUES ({0}, randomblob(32), {1}, {2}, 1, 0, '2026-01-01', '2026-01-01');
                """, id, $"user{id}", coordinator.Id);
        var repository = new BanRepository(_factory);
        var now = DateTime.UtcNow;
        var expired = new GlobalBan(Guid.NewGuid(), coordinator.Id, BanKind.Account, 101, null, "expired", 5,
            now.AddDays(-2), now.AddDays(-1), UserState.Suspended);
        var revoked = expired with { Id = Guid.NewGuid(), UserId = 102, ExpiresAt = null, PreviousState = UserState.Normal };
        var active = expired with { Id = Guid.NewGuid(), UserId = 103, ExpiresAt = null };
        var old = active with { Id = Guid.NewGuid(), CreatedAt = now.AddDays(-3), ExpiresAt = now.AddDays(-1) };
        var ip = expired with { Id = Guid.NewGuid(), Kind = BanKind.Ip, Address = "192.0.2.1" };
        foreach (var ban in new[] { expired, revoked, active, old, ip }) await repository.AddAsync(ban);
        await db.Database.ExecuteSqlRawAsync("UPDATE GlobalBans SET RevokedAt = {0}, RevokedBy = 5 WHERE Id = {1}", now, revoked.Id);

        await migrator.MigrateAsync();

        Assert.Equal(active.Id, Assert.Single(await repository.GetAsync(coordinator.Id)).Id);
        db.ChangeTracker.Clear();
        var states = await db.UserEntities.Where(u => u.CoordinatorInstanceId == coordinator.Id)
            .ToDictionaryAsync(u => u.Id, u => u.State);
        Assert.Equal(UserState.Suspended, states[101]);
        Assert.Equal(UserState.Normal, states[102]);
        Assert.Equal(UserState.Banned, states[103]);
    }

    private sealed class Factory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }
}
