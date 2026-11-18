using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Server.Application.Coordinator.Authentication;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Media;
using Server.Core.Models.Moderation;
using Server.Core.Models.TextChat;
using Server.Core.Models.User;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Entities.TextChat;
using Server.Infrastructure.Database.Repositories;
using Shared.Enums;
using Shared.Enums.User;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

[Trait("Category", "DatabaseProvider")]
public sealed class RepositoryIntegrationTests : IDisposable
{
    private readonly RepositoryTestDatabase _database = new();
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
    private const string Coordinator = "coordinator";

    public RepositoryIntegrationTests()
    {
        using var context = _database.CreateDbContext();
        context.Database.Migrate();
    }

    public void Dispose() => _database.Dispose();

    private Task<int> AddUser(string identity) => new UserRepository(_database).AddAsync(
        UserModel.Create(Coordinator, "alice", null, Now), identity);

    [Fact]
    public async Task UsersAndTokens_AreFoundByBinaryHashAndScopedToCoordinator()
    {
        var userId = await AddUser("identity");
        var users = new UserRepository(_database);
        Assert.Equal(userId, (await users.GetByIdentityAsync(Coordinator, "identity"))!.Id);
        Assert.Null(await users.GetByIdentityAsync("other", "identity"));
        await users.SetIdentityAsync(userId, "new-identity");
        Assert.Null(await users.GetByIdentityAsync(Coordinator, "identity"));
        Assert.Equal(userId, (await users.GetByIdentityAsync(Coordinator, "new-identity"))!.Id);

        var sessions = new UserSessionRepository(_database);
        var session = new UserSession(Guid.NewGuid(), Coordinator, userId, SecretHash.Of("first"), null,
            "desk", "Linux", "1.0", Now, Now, Now.AddDays(1), "127.0.0.1");
        await sessions.AddAsync(session);
        await sessions.UpdateAsync(session with { TokenHash = SecretHash.Of("second"), PreviousTokenHash = session.TokenHash });
        Assert.Equal(session.Id, (await sessions.FindByTokenAsync(Coordinator, SecretHash.Of("first"), Now))!.Id);
        Assert.Equal(session.Id, (await sessions.FindByTokenAsync(Coordinator, SecretHash.Of("second"), Now))!.Id);
        Assert.Null(await sessions.FindByTokenAsync("other", SecretHash.Of("second"), Now));
        Assert.Null(await sessions.FindByTokenAsync(Coordinator, SecretHash.Of("second"), Now.AddDays(2)));
        await sessions.DeleteExpiredAsync(Now.AddDays(2));
        Assert.Empty(await sessions.GetByUserAsync(Coordinator, userId, Now));
    }

    [Fact]
    public async Task Passkeys_RoundtripBinaryCredentialsAndUnsignedCounters_AndCascadeWithUser()
    {
        var userId = await AddUser("passkey-user");
        var repository = new UserPasskeyRepository(_database);
        byte[] credential = [0, 1, 128, 255];
        var model = UserPasskey.Create(userId, credential, [1, 2, 3], uint.MaxValue, "key", Guid.NewGuid(), Now);
        await repository.AddAsync(model);
        var saved = (await repository.GetByCredentialIdAsync(credential))!;
        Assert.Equal(uint.MaxValue, saved.SignatureCounter);
        Assert.Equal(model.PublicKey, saved.PublicKey);
        Assert.Equal(Now, saved.CreatedAt);
        saved.RecordUse(42, Now.AddMinutes(1));
        await repository.UpdateAsync(saved);
        Assert.Equal(Now.AddMinutes(1), (await repository.GetByCredentialIdAsync(credential))!.LastUsedAt);
        await new UserRepository(_database).RemoveAsync(userId);
        Assert.Null(await repository.GetByCredentialIdAsync(credential));
    }

    [Fact]
    public async Task DirectChats_AreUniquePerCoordinator_AndFailedInsertLeavesNoSession()
    {
        var first = await AddUser("one");
        var second = await AddUser("two");
        var repository = new ChatSessionRepository(_database);
        ChatSessionModel Direct(string coordinator)
        {
            var chat = ChatSessionModel.Create(coordinator, "direct", first);
            chat.SetAudience(ChatKind.Direct, [first, second]);
            return chat;
        }
        var id = await repository.AddAsync(Direct(Coordinator));
        await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddAsync(Direct(Coordinator)));
        await repository.AddAsync(Direct("other"));
        await repository.AddAsync(ChatSessionModel.Create(Coordinator, "general", first));
        await repository.AddAsync(ChatSessionModel.Create(Coordinator, "second", first));
        await using var context = _database.CreateDbContext();
        Assert.Equal(4, await context.Sessions.CountAsync());
        Assert.True((await repository.GetByIdAsync(id))!.MemberIds.SetEquals([first, second]));
        await repository.RemoveAsync(id);
        Assert.Empty(await context.ChatSessionMembers.Where(x => x.ChatSessionId == id).ToListAsync());
    }

    [Fact]
    public async Task CoordinatorSettings_RoundtripListsJsonAndUtcTimes_AfterUpdate()
    {
        var repository = new CoordinatorInstanceRepository(_database);
        var model = CoordinatorInstanceModel.Create("node", "127.0.0.1", 26666, Now);
        model.SetAccessList(new AccessListSettings(["192.0.2.1", "::1"], ["192.0.2.2"]));
        await repository.AddAsync(model);
        model.SetAccessList(new AccessListSettings(["192.0.2.3"], []));
        model.MarkLaunched(Now.AddHours(1));
        await repository.UpdateAsync(model);
        var saved = (await repository.GetByIdAsync(model.Id))!;
        Assert.Equal(["192.0.2.3"], saved.AccessList.AllowedIps);
        Assert.Empty(saved.AccessList.BannedIps);
        Assert.Equal(model.RateLimits, saved.RateLimits);
        Assert.Equal(Now.AddHours(1), saved.CurrentLaunchTime);
    }

    [Fact]
    public async Task BansAndUploads_PreserveScopeAndStateDuringCleanup()
    {
        var bans = new BanRepository(_database);
        var ban = new GlobalBan(Guid.NewGuid(), Coordinator, BanKind.Ip, null, "192.0.2.1", "abuse", 1,
            Now, Now.AddDays(1), UserState.Normal);
        await bans.AddAsync(ban);
        Assert.Equal(ban, Assert.Single(await bans.GetAsync(Coordinator)));
        await bans.DeleteAsync("other", ban.Id);
        Assert.Single(await bans.GetAsync(Coordinator));
        await bans.DeleteAsync(Coordinator, ban.Id);
        Assert.Empty(await bans.GetAsync(Coordinator));

        var uploads = new MediaUploadRepository(_database);
        MediaUploadModel Upload(string id, string coordinator = Coordinator) => new(id, coordinator, 1, null,
            MediaUploadKind.File, "text/plain", 100, MediaUploadState.Uploading, Now);
        await uploads.AddAsync(Upload("attached"));
        await uploads.AddAsync(Upload("old"));
        await uploads.AddAsync(Upload("other", "other"));
        Assert.False(await uploads.MarkAttachedAsync("attached"));
        Assert.True(await uploads.MarkCompletedAsync("attached"));
        Assert.False(await uploads.MarkCompletedAsync("attached"));
        Assert.True(await uploads.MarkAttachedAsync("attached"));
        Assert.Equal(200, await uploads.GetTotalSizeAsync(Coordinator));
        Assert.Equal(1, await uploads.CountUnattachedAsync(Coordinator, 1));
        Assert.Equal(["old"], await uploads.RemoveUnattachedAsync(Coordinator, Now.AddDays(1)));
        Assert.NotNull(await uploads.GetAsync("attached"));
        Assert.NotNull(await uploads.GetAsync("other"));
        Assert.Equal(100, await uploads.GetTotalSizeAsync(Coordinator));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavedTimestamps_MatchSubsequentReads_ForSyncAndAsyncSaves(bool synchronous)
    {
        await using var context = _database.CreateDbContext();
        var preview = new LinkPreviewEntity
        {
            CoordinatorInstanceId = Coordinator, Url = "https://example.com", FetchedAt = Now.AddTicks(7)
        };
        context.LinkPreviews.Add(preview);
        if (synchronous) context.SaveChanges(); else await context.SaveChangesAsync();
        await using var reader = _database.CreateDbContext();
        var read = await reader.LinkPreviews.SingleAsync(x => x.Id == preview.Id);
        Assert.Equal(preview.FetchedAt, read.FetchedAt);
        Assert.Equal(context.Database.IsNpgsql() ? Now : Now.AddTicks(7), read.FetchedAt);
        preview.FetchedAt = DateTime.MaxValue;
        if (synchronous) context.SaveChanges(); else await context.SaveChangesAsync();
        Assert.Equal(DateTime.MaxValue, await reader.LinkPreviews.AsNoTracking()
            .Where(x => x.Id == preview.Id).Select(x => x.FetchedAt).SingleAsync());
    }

    [Fact]
    public async Task Initializer_IsRepeatable_AndSchemaCanBeRolledBackAndRecreated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<ServerDbContext>>(_database);
        await using var provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync();
        await provider.InitializeDatabaseAsync();
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        await context.GetService<IMigrator>().MigrateAsync("0");
        await provider.InitializeDatabaseAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await context.UserEntities.CountAsync());
    }
}
