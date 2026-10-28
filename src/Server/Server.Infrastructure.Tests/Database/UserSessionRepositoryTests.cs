using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Server.Application.Coordinator.Authentication;
using Server.Core.Models.User;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Repositories;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class UserSessionRepositoryTests : IDisposable
{
    private const string Coordinator = "coordinator";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"sessions-{Guid.NewGuid():N}.db");
    private readonly TestContextFactory _factory;
    private readonly DateTime _now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    public UserSessionRepositoryTests()
    {
        _factory = new TestContextFactory(new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    /// <summary>A database as the previous version left it, with one user, migrated to the current one.</summary>
    private async Task<int> MigrateWithLegacyUser(string identity)
    {
        await using var context = _factory.CreateDbContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("PasskeyLogins");
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "UserEntities" ("UserIdentity", "Username", "CoordinatorInstanceId", "State", "LastStatus",
                "FirstConnectionTime", "LastConnectionTime")
            VALUES ({0}, 'alice', {1}, 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
            """, identity, Coordinator);
        await migrator.MigrateAsync();
        return await context.UserEntities.Select(x => x.Id).SingleAsync();
    }

    private async Task<int> MigrateWithUser()
    {
        await using var context = _factory.CreateDbContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "UserEntities" ("IdentityHash", "Username", "CoordinatorInstanceId", "State", "LastStatus",
                "FirstConnectionTime", "LastConnectionTime")
            VALUES (X'00', 'alice', {0}, 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
            """, Coordinator);
        return await context.UserEntities.Select(x => x.Id).SingleAsync();
    }

    private UserSession Session(int userId, string token, DateTime? expiresAt = null) =>
        new(Guid.NewGuid(), Coordinator, userId, SecretHash.Of(token), null, "desk", "Linux", "0.8.0", _now, _now,
            expiresAt ?? _now.AddDays(90), "10.0.0.1");

    [Fact]
    public async Task Migration_KeepsOnlyTheHashOfAnIdentity_AndTheUserStillLogsInWithIt()
    {
        var userId = await MigrateWithLegacyUser("#legacy-identity");

        var users = new UserRepository(_factory);
        var user = await users.GetByIdentityAsync(Coordinator, "#legacy-identity");

        Assert.Equal(userId, user?.Id);
        Assert.Null(await users.GetByIdentityAsync(Coordinator, "#another"));
        await using var context = _factory.CreateDbContext();
        Assert.Equal(SecretHash.Of("#legacy-identity"),
            await context.UserEntities.Select(x => x.IdentityHash).SingleAsync());
    }

    [Fact]
    public async Task Migration_RunsOnThePooledContextsOfTheServer()
    {
        await using (var context = _factory.CreateDbContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("PasskeyLogins");
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO "UserEntities" ("UserIdentity", "Username", "CoordinatorInstanceId", "State", "LastStatus",
                    "FirstConnectionTime", "LastConnectionTime")
                VALUES ('#pooled', 'alice', {0}, 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
                """, Coordinator);
        }

        // As Program registers them: a pooled context must not change its options
        var services = new ServiceCollection()
            .AddPooledDbContextFactory<ServerDbContext>(options => options.UseSqlite($"Data Source={_databasePath}")
                .AddInterceptors(new SqliteSynchronousInterceptor()))
            .BuildServiceProvider();
        var pooled = services.GetRequiredService<IDbContextFactory<ServerDbContext>>();
        await using (var context = await pooled.CreateDbContextAsync())
            await context.Database.MigrateAsync();

        Assert.NotNull(await new UserRepository(pooled).GetByIdentityAsync(Coordinator, "#pooled"));
    }

    [Fact]
    public async Task NewIdentity_ReplacesTheOldOne()
    {
        var userId = await MigrateWithLegacyUser("#old");
        var users = new UserRepository(_factory);

        await users.SetIdentityAsync(userId, "#new");

        Assert.Null(await users.GetByIdentityAsync(Coordinator, "#old"));
        Assert.Equal(userId, (await users.GetByIdentityAsync(Coordinator, "#new"))?.Id);
    }

    [Fact]
    public async Task Session_IsFoundByItsTokenOrThePreviousOne_UntilItExpires()
    {
        var userId = await MigrateWithUser();
        var repository = new UserSessionRepository(_factory);
        var session = Session(userId, "first");
        await repository.AddAsync(session);
        await repository.UpdateAsync(session with
        {
            TokenHash = SecretHash.Of("second"), PreviousTokenHash = SecretHash.Of("first")
        });

        Assert.Equal(session.Id, (await repository.FindByTokenAsync(Coordinator, SecretHash.Of("first"), _now))?.Id);
        Assert.Equal(session.Id, (await repository.FindByTokenAsync(Coordinator, SecretHash.Of("second"), _now))?.Id);
        Assert.Null(await repository.FindByTokenAsync("other", SecretHash.Of("second"), _now));
        Assert.Null(await repository.FindByTokenAsync(Coordinator, SecretHash.Of("second"), _now.AddDays(91)));
    }

    [Fact]
    public async Task DeleteAllBut_KeepsOneSession_AndDeleteAllButNothing_KeepsNone()
    {
        var userId = await MigrateWithUser();
        var repository = new UserSessionRepository(_factory);
        var kept = Session(userId, "a");
        await repository.AddAsync(kept);
        await repository.AddAsync(Session(userId, "b"));
        await repository.AddAsync(Session(userId, "c"));

        Assert.Equal(2, (await repository.DeleteAllButAsync(Coordinator, userId, kept.Id)).Count);
        Assert.Equal(kept.Id, Assert.Single(await repository.GetByUserAsync(Coordinator, userId, _now)).Id);

        Assert.Single(await repository.DeleteAllButAsync(Coordinator, userId, null));
        Assert.Empty(await repository.GetByUserAsync(Coordinator, userId, _now));
    }

    [Fact]
    public async Task ExpiredSessions_AreDeleted()
    {
        var userId = await MigrateWithUser();
        var repository = new UserSessionRepository(_factory);
        var alive = Session(userId, "alive");
        await repository.AddAsync(alive);
        await repository.AddAsync(Session(userId, "old", _now.AddDays(-1)));

        await repository.DeleteExpiredAsync(_now);

        Assert.Equal(alive.Id, Assert.Single(await repository.GetByUserAsync(Coordinator, userId, _now.AddDays(-30))).Id);
    }

    private sealed class TestContextFactory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }
}
