using System.Text.Json;
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
