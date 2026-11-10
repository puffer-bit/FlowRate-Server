using Shared.Models.DataTransferObjects.ChatSession.Messages;
using Server.Core.Models.TextChat.Messages;
using System.Text.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Media;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Handlers.Sessions;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Layout;
using Server.Application.Coordinator.Users;
using Server.Core.Models.User;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.DataTransferObjects.ChatSession;
using Shared.Models.Requests.SessionActions.Generic;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Shared.Models.Requests.SessionActions.Generic.Models.SessionData;
using Shared.Models.Responses.SessionActions.Generic;
using Shared.Serialization;
using Shared.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Server.Core.Models.TextChat;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Entities;
using Server.Infrastructure.Database.Entities.TextChat;
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
        context.UserEntities.AddRange(Enumerable.Range(1, 6).Select(id => new UserEntity
        {
            Id = id, Username = $"User {id}", IdentityHash = [(byte)id], CoordinatorInstanceId = "coordinator"
        }));
        context.SaveChanges();
    }

    [Fact]
    public async Task MembershipAndSystemMessages_CommitTogether_WithOrderedRevisions()
    {
        var group = ChatSessionModel.Create("coordinator", "Group", 1);
        group.SetAudience(ChatKind.Group, [1, 2]);
        group.Id = await _repository.AddAsync(group);
        group.SetAudience(ChatKind.Group, [1, 3]);
        ChatMessageModel Message(ChatSystemEventKind kind, int subject) => ChatMessageModel.CreateSystem(group.Id,
            new ChatSystemContent { Kind = kind, ActorUserId = 1, SubjectUserId = subject }, DateTime.UtcNow);
        var stored = await _repository.UpdateWithSystemMessagesAsync(group,
            [Message(ChatSystemEventKind.MemberAdded, 3), Message(ChatSystemEventKind.MemberRemoved, 2)]);
        Assert.True((await _repository.GetByIdAsync(group.Id))!.MemberIds.SetEquals([1, 3]));
        var messages = new MessagesRepository(_factory);
        Assert.Equal(new long[] { 1, 2 }, stored.Select(message => message.Revision));
        Assert.True(stored[0].Id < stored[1].Id);
        Assert.Equal(stored.Select(message => message.SystemContent),
            (await messages.GetBeforeAsync(group.Id, null, 10)).Select(message => message.SystemContent));
        Assert.Equal(2, await messages.GetRevisionAsync(group.Id));

        // The first message is inserted successfully, then a duplicate ID fails the second insert.
        // Both that insert and the membership change must roll back.
        group.SetAudience(ChatKind.Group, [1, 4]);
        var duplicate = Message(ChatSystemEventKind.MemberRemoved, 3);
        duplicate.Id = stored[0].Id;
        await Assert.ThrowsAsync<DbUpdateException>(() => _repository.UpdateWithSystemMessagesAsync(group,
            [Message(ChatSystemEventKind.MemberAdded, 4), duplicate]));
        Assert.True((await _repository.GetByIdAsync(group.Id))!.MemberIds.SetEquals([1, 3]));
        Assert.Equal(2, await messages.GetRevisionAsync(group.Id));
        Assert.Equal(2, (await messages.GetBeforeAsync(group.Id, null, 10)).Count);
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

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task GroupCreation_PassesProtocolAuthorizationHandlerAndDatabase(int peerCount)
    {
        var peers = Enumerable.Range(2, peerCount).ToArray();
        var events = Mock.Of<ICoordinatorEventBus>();
        var messages = Mock.Of<IMessagesRepository>();
        ChatSessionManager Chats() => new(NullLogger.Instance, events, _repository, messages,
            Mock.Of<IMediaUploadRepository>(), Mock.Of<IMediaStorage>(), "coordinator");
        var chats = Chats();
        var layout = new Mock<ISessionLayoutService>(MockBehavior.Strict);
        using var sessions = new SessionManager(chats, Mock.Of<IHybridSessionManager>(), layout.Object,
            Mock.Of<ISessionPermissionsRepository>(), "coordinator");
        var users = new Mock<IUserManager>();
        users.Setup(manager => manager.GetUserAsync(It.IsAny<int>())).ReturnsAsync((int id) =>
            new UserModel(id, "coordinator", $"User {id}", UserState.Normal, UserStatus.Online,
                DateTime.UtcNow, DateTime.UtcNow));
        BaseMessage? answer = null;
        var sender = new Mock<IMessageSender>();
        sender.Setup(s => s.SendMessageById("device", It.IsAny<BaseMessage>()))
            .Callback<string, BaseMessage>((_, response) => answer = response);
        var handler = new CreateSessionHandler(sessions, sender.Object, users.Object);
        var authorizer = new RequestAuthorizer(Mock.Of<ICoordinatorPermissions>(), Mock.Of<IHybridSessionManager>(), sessions);
        for (var index = 0; index < 2; index++)
        {
            BaseMessage request = new CreateSessionRequest(new CreateSessionModel("Friends", -1,
                new ChatSessionCreateData(Kind: ChatKind.Group, MemberIds: peers))) { SenderId = 1 };
            var json = JsonSerializer.Serialize(request, ProtocolJsonContext.Default.BaseMessage);
            var decoded = Assert.IsType<CreateSessionRequest>(JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.BaseMessage));
            Assert.True(MessageValidator.TryValidate(decoded, out var errors), string.Join("; ", errors));
            Assert.True(await authorizer.IsAllowedAsync(decoded));
            await handler.HandleMessage(decoded, "device");
            var response = Assert.IsType<CreateSessionResponse>(answer);
            var wire = JsonSerializer.Serialize<BaseMessage>(response, ProtocolJsonContext.Default.BaseMessage);
            var returned = Assert.IsType<CreateSessionResponse>(JsonSerializer.Deserialize(wire, ProtocolJsonContext.Default.BaseMessage));
            var group = Assert.IsType<ChatSessionDTO>(returned.Session);
            Assert.Equal(ChatKind.Group, group.Kind);
            Assert.Equal(peers.Prepend(1), group.MemberIds);
            var saved = await _repository.GetByIdAsync(group.Id);
            Assert.True(saved!.MemberIds.SetEquals(peers.Prepend(1)));
            Assert.Equal(1, saved.AdminUserId);
        }
        var reloaded = Chats();
        await reloaded.LoadAsync();
        Assert.Equal(2, reloaded.GetAllSessions().Count);
        Assert.All(reloaded.GetAllSessions(), group => Assert.Equal(ChatKind.Group, group.Kind));
        layout.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChatListsLoadMembershipInOneQueryRegardlessOfChatCount()
    {
        for (var index = 0; index < 3; index++)
        {
            var group = ChatSessionModel.Create("coordinator", $"Group {index}", 1);
            group.SetAudience(ChatKind.Group, [1, 2, 3]);
            await _repository.AddAsync(group);
        }
        var counter = new QueryCounter();
        var factory = new Factory(new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite($"Data Source={_path}").AddInterceptors(counter).Options);
        var repository = new ChatSessionRepository(factory);
        var chats = await repository.GetByCoordinatorIdAsync("coordinator");
        Assert.Equal(1, counter.Reads);
        Assert.Equal(3, chats.Count);
        Assert.All(chats, chat => Assert.True(chat.MemberIds.SetEquals([1, 2, 3])));
        counter.Reads = 0;
        Assert.Equal(3, (await repository.GetAllAsync()).Count);
        Assert.Equal(1, counter.Reads);
    }

    [Fact]
    public async Task FailedMembershipUpdateRollsBackChatAndMembersTogether()
    {
        var group = ChatSessionModel.Create("coordinator", "Friends", 1);
        group.SetAudience(ChatKind.Group, [1, 2]);
        group.Id = await _repository.AddAsync(group);
        group.SetAudience(ChatKind.Group, [1, 99]);
        group.Rename("Rejected");
        await Assert.ThrowsAsync<DbUpdateException>(() => _repository.UpdateAsync(group));
        var saved = await _repository.GetByIdAsync(group.Id);
        Assert.Equal("Friends", saved!.Name);
        Assert.True(saved.MemberIds.SetEquals([1, 2]));
    }

    [Fact]
    public async Task MembershipUpdatesOnlyChangedRowsAndPreservesRevisionAndJoiningDate()
    {
        var group = ChatSessionModel.Create("coordinator", "Friends", 1);
        group.SetAudience(ChatKind.Group, [1, 2]);
        group.Id = await _repository.AddAsync(group);
        var joined = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var context = _factory.CreateDbContext())
        {
            var membership = await context.ChatSessionMembers.SingleAsync(member => member.ChatSessionId == group.Id && member.UserId == 1);
            membership.JoinedAt = joined;
            await context.SaveChangesAsync();
            await context.ChatSessionEntities.Where(chat => chat.Id == group.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(chat => chat.LastRevision, 42));
        }
        group.SetAudience(ChatKind.Group, [1, 3]);
        group.ChangeAdmin(3);
        group.Rename("Renamed");
        await _repository.UpdateAsync(group);
        await using (var context = _factory.CreateDbContext())
        {
            var rows = await context.ChatSessionMembers.Where(member => member.ChatSessionId == group.Id).OrderBy(member => member.UserId).ToListAsync();
            Assert.Equal(new[] { 1, 3 }, rows.Select(member => member.UserId));
            Assert.Equal(joined, rows[0].JoinedAt);
            Assert.True(rows[1].JoinedAt > joined);
            Assert.Equal(42, await context.ChatSessionEntities.Where(chat => chat.Id == group.Id).Select(chat => chat.LastRevision).SingleAsync());
        }
        var saved = await _repository.GetByIdAsync(group.Id);
        Assert.Equal(3, saved!.AdminUserId);
        Assert.Equal("Renamed", saved.Name);
        Assert.True(saved.MemberIds.SetEquals([1, 3]));
    }

    [Fact]
    public async Task MembershipConstraintsAndChatDeletionAreEnforcedByDatabase()
    {
        var group = ChatSessionModel.Create("coordinator", "Friends", 1);
        group.SetAudience(ChatKind.Group, [1, 2]);
        group.Id = await _repository.AddAsync(group);
        await using (var context = _factory.CreateDbContext())
        {
            context.ChatSessionMembers.Add(new ChatSessionMemberEntity { ChatSessionId = group.Id, UserId = 1, AccountId = 1 });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        await using (var context = _factory.CreateDbContext())
        {
            context.ChatSessionMembers.Add(new ChatSessionMemberEntity { ChatSessionId = group.Id, UserId = 99, AccountId = 99 });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        await using (var context = _factory.CreateDbContext())
        {
            context.ChatSessionMembers.Add(new ChatSessionMemberEntity { ChatSessionId = int.MaxValue, UserId = 1, AccountId = 1 });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        await _repository.RemoveAsync(group.Id);
        await using var verify = _factory.CreateDbContext();
        Assert.Empty(await verify.ChatSessionMembers.ToListAsync());
    }

    [Fact]
    public async Task DeletingAnAccountPreservesHistoricalMembershipAndDirectChatReload()
    {
        var direct = ChatSessionModel.Create("coordinator", "Direct", 1);
        direct.SetAudience(ChatKind.Direct, [1, 2]);
        direct.Id = await _repository.AddAsync(direct);
        await using (var context = _factory.CreateDbContext())
        {
            await context.UserEntities.Where(user => user.Id == 2).ExecuteDeleteAsync();
            var row = await context.ChatSessionMembers.SingleAsync(member => member.ChatSessionId == direct.Id && member.UserId == 2);
            Assert.Null(row.AccountId);
        }
        var saved = await _repository.GetByIdAsync(direct.Id);
        Assert.True(saved!.MemberIds.SetEquals([1, 2]));
        saved.Rename("Retained history");
        await _repository.UpdateAsync(saved);
        Assert.True((await _repository.GetAllAsync()).Single().MemberIds.SetEquals([1, 2]));
    }

    [Fact]
    public async Task MembershipMigrationPreservesExistingChatsAndDeletedAccountsOnUpgradeAndRollback()
    {
        await using (var context = _factory.CreateDbContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("20261110153651_PrivateChats");
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO Sessions (Id, Name, CoordinatorInstanceId, Capacity)
                    VALUES (500, 'Group', 'coordinator', -1), (501, 'Direct', 'coordinator', -1);
                INSERT INTO ChatSessions (Id, CreatorUserId, AdminUserId, LastRevision, IsReadOnly, IsArchived,
                    IsAttachmentsAllowed, IsVoiceMessagesAllowed, IsVideoMessagesAllowed, IsDelayedMessagesAllowed,
                    Kind, MemberIdsJson, DirectKey)
                    VALUES (500, 1, 1, 7, 0, 0, 1, 1, 1, 1, 2, '[1,2,2,99]', NULL),
                           (501, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, '[1,2]', 'coordinator:1:2');
                """);
            await context.Database.MigrateAsync();
            Assert.False(context.Database.HasPendingModelChanges());
            Assert.Equal(5, await context.ChatSessionMembers.CountAsync());
            Assert.Null((await context.ChatSessionMembers.SingleAsync(member => member.ChatSessionId == 500 && member.UserId == 99)).AccountId);
            Assert.Equal(7, (await context.ChatSessionEntities.SingleAsync(chat => chat.Id == 500)).LastRevision);
            Assert.DoesNotContain("MemberIdsJson", await context.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('ChatSessions')").ToListAsync());
            Assert.Empty(await context.Database.SqlQueryRaw<string>("SELECT [table] AS Value FROM pragma_foreign_key_check").ToListAsync());
        }
        Assert.True((await _repository.GetByIdAsync(500))!.MemberIds.SetEquals([1, 2, 99]));
        Assert.True((await _repository.GetByIdAsync(501))!.MemberIds.SetEquals([1, 2]));
        await using (var context = _factory.CreateDbContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("20261110153651_PrivateChats");
            Assert.Equal("[1,2,99]", await context.Database.SqlQueryRaw<string>("SELECT MemberIdsJson AS Value FROM ChatSessions WHERE Id = 500").SingleAsync());
            await context.Database.MigrateAsync();
        }
        Assert.True((await _repository.GetByIdAsync(500))!.MemberIds.SetEquals([1, 2, 99]));
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
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Factory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
        public Task<ServerDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
