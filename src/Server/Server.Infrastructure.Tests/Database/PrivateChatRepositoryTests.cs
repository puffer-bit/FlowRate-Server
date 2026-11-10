using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Server.Core.Models.TextChat;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Repositories;
using Shared.Enums;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class PrivateChatRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"private-chats-{Guid.NewGuid():N}.db");
    private readonly Factory _factory;
    private readonly ChatSessionRepository _repository;

    public PrivateChatRepositoryTests()
    {
        _factory = new Factory(new DbContextOptionsBuilder<ServerDbContext>().UseSqlite($"Data Source={_path}").Options);
        _repository = new ChatSessionRepository(_factory);
        using var context = _factory.CreateDbContext();
        context.Database.Migrate();
    }

    [Fact]
    public async Task MembershipAndOwner_SurviveReload_AndDatabaseRejectsDuplicatePair()
    {
        var direct = ChatSessionModel.Create("coordinator", "Direct", 1);
        direct.SetAudience(ChatKind.Direct, [2, 1]);
        direct.Id = await _repository.AddAsync(direct);
        var saved = await _repository.GetByIdAsync(direct.Id);
        Assert.Equal(ChatKind.Direct, saved!.Kind);
        Assert.True(saved.MemberIds.SetEquals([1, 2]));
        var duplicate = ChatSessionModel.Create("coordinator", "Reversed", 2);
        duplicate.SetAudience(ChatKind.Direct, [1, 2]);
        await Assert.ThrowsAsync<DbUpdateException>(() => _repository.AddAsync(duplicate));
        var otherServer = ChatSessionModel.Create("other", "Other server", 1);
        otherServer.SetAudience(ChatKind.Direct, [1, 2]);
        await _repository.AddAsync(otherServer);
    }

    [Fact]
    public async Task Migration_PreservesExistingServerChannels()
    {
        using var context = _factory.CreateDbContext();
        await context.GetService<IMigrator>().MigrateAsync("20261110120000_ChatCommandResults");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO Sessions (Id, Name, CoordinatorInstanceId, Capacity) VALUES (500, 'General', 'coordinator', -1);
            INSERT INTO ChatSessions (Id, CreatorUserId, AdminUserId, LastRevision, IsReadOnly, IsArchived,
                IsAttachmentsAllowed, IsVoiceMessagesAllowed, IsVideoMessagesAllowed, IsDelayedMessagesAllowed)
                VALUES (500, 1, 1, 0, 0, 0, 1, 1, 1, 1);
            """);
        await context.Database.MigrateAsync();
        var saved = await _repository.GetByIdAsync(500);
        Assert.Equal(ChatKind.ServerChannel, saved!.Kind);
        Assert.Empty(saved.MemberIds);
        Assert.Equal("General", saved.Name);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); File.Delete(_path); }
    private sealed class Factory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
        public Task<ServerDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
