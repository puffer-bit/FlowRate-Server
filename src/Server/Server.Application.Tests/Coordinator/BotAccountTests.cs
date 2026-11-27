using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Handlers.Users;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Users;
using Server.Application.Tests.Fakes;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.Requests.Coordinator;
using Shared.Models.Requests.Coordinator.Security;
using Shared.Models.Responses;
using Shared.Models.Responses.Coordinator;

namespace Server.Application.Tests.Coordinator;

/// <summary>Bot accounts in the user manager: making them, their limit, and what sets them apart from people.</summary>
public class BotUserManagerTests
{
    private const string CoordinatorId = "coordinator";
    private const int OwnerId = 1;

    private readonly Mock<IUserRepository> _users = new();
    private readonly UserManagerContext _context = new();
    private readonly ConnectionsContext _connections = new();
    private readonly CoordinatorInstanceContext _coordinator = new(Model(maxOnlineUsers: 1, maxBots: 2), "1.0");
    private readonly UserManager _manager;
    private int _nextId = 100;

    public BotUserManagerTests()
    {
        _users.Setup(r => r.AddAsync(It.IsAny<UserModel>(), It.IsAny<UserRecoveryKeys?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _nextId++);
        _users.Setup(r => r.CountAsync(CoordinatorId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _users.Setup(r => r.CountBotsAsync(CoordinatorId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _users.Setup(r => r.ClearBotOwnerAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        _manager = new UserManager(_context, NullLogger.Instance, CoordinatorId, _users.Object, _coordinator,
            _connections);
    }

    [Theory]
    [InlineData(false, UserKind.Bot)]
    [InlineData(true, UserKind.DebugBot)]
    public async Task CreateBot_SavesABotOwnedByTheSender_WithoutRecoveryKeys(bool isDebug, UserKind kind)
    {
        var (error, bot) = await _manager.CreateBot(OwnerId, "  Helper  ", isDebug);

        Assert.Null(error);
        Assert.NotNull(bot);
        Assert.Equal("Helper", bot.Username);
        Assert.Equal(kind, bot.Kind);
        Assert.Equal(OwnerId, bot.OwnerUserId);
        Assert.Equal(100, bot.Id);
        Assert.False(bot.IsConnected);
        _users.Verify(r => r.AddAsync(It.Is<UserModel>(u => u.Kind == kind), null, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task CreateBot_AtTheLimit_IsRefused_ButADebugBotIsNotCounted()
    {
        _users.Setup(r => r.CountBotsAsync(CoordinatorId, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        Assert.Equal(ErrorCode.BotLimitReached, (await _manager.CreateBot(OwnerId, "Helper", isDebug: false)).Error);
        Assert.Null((await _manager.CreateBot(OwnerId, "Tester", isDebug: true)).Error);
    }

    [Fact]
    public async Task CreateBot_WithALimitOfZero_MakesNoBot()
    {
        _coordinator.ApplySettings(Model(maxOnlineUsers: 1, maxBots: 0));

        Assert.Equal(ErrorCode.BotLimitReached, (await _manager.CreateBot(OwnerId, "Helper", isDebug: false)).Error);
    }

    [Fact]
    public async Task CreateBot_WithASavedName_IsRefused()
    {
        _users.Setup(r => r.IsUsernameTakenAsync("Helper", 0, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.Equal(ErrorCode.NameTaken, (await _manager.CreateBot(OwnerId, "Helper", isDebug: true)).Error);
        _users.Verify(r => r.AddAsync(It.IsAny<UserModel>(), It.IsAny<UserRecoveryKeys?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateBot_WithTheNameOfAConnectedUser_IsRefused_WhateverTheCase()
    {
        _context.AttachedUsers[5] = Person(5, "helper");

        Assert.Equal(ErrorCode.NameTaken, (await _manager.CreateBot(OwnerId, "HELPER", isDebug: true)).Error);
    }

    [Fact]
    public async Task CreateBot_WithABlankName_IsRefused()
    {
        Assert.Equal(ErrorCode.InvalidValue, (await _manager.CreateBot(OwnerId, "   ", isDebug: true)).Error);
    }

    [Fact]
    public async Task Bot_TakesNoPlaceAmongTheOnlineUsers()
    {
        _context.AttachedUsers[5] = Person(5, "alice");
        var bot = Bot(7);
        _users.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(bot);

        var attachment = await _manager.AttachUser(7, Connection("bot"));

        Assert.Equal(AttachUserResult.Attached, attachment.Result);
    }

    [Fact]
    public async Task OnlineBot_DoesNotKeepAPersonOut()
    {
        _context.AttachedUsers[7] = Bot(7);
        var person = Person(5, "alice");
        _users.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(person);

        var attachment = await _manager.AttachUser(5, Connection("alice"));

        Assert.Equal(AttachUserResult.Attached, attachment.Result);
    }

    [Fact]
    public async Task Bot_IsNeverIdle()
    {
        var bot = Bot(7);
        _users.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(bot);
        await _manager.AttachUser(7, Connection("bot"));

        Assert.Null(_manager.SetIdleLevel(7, "bot", IdleLevel.Sleeping));
        Assert.Equal(IdleLevel.Active, bot.IdleLevel);
    }

    [Fact]
    public async Task DeletingTheOwner_LeavesTheConnectedBotsWithoutAnOwner_AndTellsSo()
    {
        var owner = Person(OwnerId, "owner");
        _users.Setup(r => r.GetByIdAsync(OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(owner);
        _users.Setup(r => r.ClearBotOwnerAsync(OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync([7, 8]);
        var bot = Bot(7);
        _context.AttachedUsers[7] = bot;
        var updated = new List<int>();
        _manager.UserUpdated += user => updated.Add(user.Id);

        Assert.Null(await _manager.DeleteUser(OwnerId));

        Assert.Null(bot.OwnerUserId);
        Assert.Equal(new[] { 7 }, updated);
    }

    [Fact]
    public async Task GetBots_ShowsTheConnectedOnesAsTheyAreAttached()
    {
        var saved = Bot(7);
        var attached = Bot(7);
        _users.Setup(r => r.GetBotsAsync(CoordinatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([saved, Bot(8)]);
        _context.AttachedUsers[7] = attached;

        var bots = await _manager.GetBotsAsync();

        Assert.Same(attached, bots[0]);
        Assert.Equal(8, bots[1].Id);
    }

    private static UserModel Person(int id, string name)
    {
        var user = UserModel.Create(CoordinatorId, name, "127.0.0.1", DateTimeOffset.UtcNow);
        user.Id = id;
        return user;
    }

    private static UserModel Bot(int id)
    {
        var bot = UserModel.CreateBot(CoordinatorId, $"bot{id}", OwnerId, isDebug: false, DateTimeOffset.UtcNow);
        bot.Id = id;
        return bot;
    }

    private static IClientConnection Connection(string id)
    {
        var connection = new Mock<IClientConnection>();
        connection.SetupGet(c => c.ConnectionId).Returns(id);
        connection.SetupGet(c => c.IpEndpoint).Returns("127.0.0.1");
        connection.SetupProperty(c => c.UserId);
        return connection.Object;
    }

    private static CoordinatorInstanceModel Model(int maxOnlineUsers, int maxBots) =>
        new(CoordinatorId, "coordinator", true, "127.0.0.1", 5000, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, maxOnlineUsers, 10, MotdSettings.Disabled, AccessListSettings.Open,
            new CoordinatorHeartbeatSettings(5, 40), CoordinatorMediaSettings.Default, CoordinatorUploadSettings.Default,
            CoordinatorRateLimitSettings.Default, [], maxBots: maxBots);
}

/// <summary>What a bot may not do whatever its roles, and who may manage bots.</summary>
public class BotRequestAuthorizerTests
{
    private const int PersonId = 1;
    private const int BotId = 2;
    private const int ManagerId = 3;
    private const int HigherBotId = 4;

    private readonly RequestAuthorizer _authorizer;

    public BotRequestAuthorizerTests()
    {
        var permissions = new Mock<ICoordinatorPermissions>();
        var granted = new Dictionary<int, Permission>
        {
            [PersonId] = Permission.Default,
            [BotId] = Permission.All,
            [ManagerId] = Permission.Default | Permission.ManageBots,
            [HigherBotId] = Permission.Default
        };
        var positions = new Dictionary<int, int> { [PersonId] = 0, [BotId] = 0, [ManagerId] = 1, [HigherBotId] = 2 };
        permissions.Setup(p => p.Has(It.IsAny<int>(), It.IsAny<Permission>()))
            .Returns((int userId, Permission permission) => (granted[userId] & permission) == permission);
        permissions.Setup(p => p.HighestPositionOfAsync(It.IsAny<int>()))
            .ReturnsAsync((int userId) => positions[userId]);

        _authorizer = new RequestAuthorizer(permissions.Object, Mock.Of<IHybridSessionManager>(),
            Mock.Of<ISessionManager>(), userId => userId is BotId or HigherBotId);
    }

    private static T From<T>(int senderId, T message) where T : BaseMessage
    {
        message.SenderId = senderId;
        return message;
    }

    [Fact]
    public async Task Bot_MayNotManageBots_EvenWithEveryPermission()
    {
        Assert.False(await _authorizer.IsAllowedAsync(From(BotId, new CoordinatorBotsRequest())));
        Assert.False(await _authorizer.IsAllowedAsync(From(BotId, new CoordinatorCreateBotRequest("Helper"))));
    }

    [Fact]
    public async Task Bot_MayNotTouchTheSecurityOfAnAccount()
    {
        Assert.False(await _authorizer.IsAllowedAsync(From(BotId, new UserSecurityInfoRequest())));
        Assert.True(await _authorizer.IsAllowedAsync(From(PersonId, new UserSecurityInfoRequest())));
    }

    [Fact]
    public async Task Bot_MayDoWhatItsRolesAllow()
    {
        Assert.True(await _authorizer.IsAllowedAsync(From(BotId, new CoordinatorInfoRequest())));
    }

    [Fact]
    public async Task ManagingBots_NeedsThePermission()
    {
        Assert.False(await _authorizer.IsAllowedAsync(From(PersonId, new CoordinatorBotsRequest())));
        Assert.True(await _authorizer.IsAllowedAsync(From(ManagerId, new CoordinatorBotsRequest())));
        Assert.True(await _authorizer.IsAllowedAsync(From(ManagerId, new CoordinatorCreateBotRequest("Helper"))));
    }

    [Fact]
    public async Task ACodeOrADeletion_KeepsToTheRoleHierarchy()
    {
        Assert.True(await _authorizer.IsAllowedAsync(From(ManagerId, new CoordinatorBotLoginCodeRequest(BotId))));
        Assert.True(await _authorizer.IsAllowedAsync(From(ManagerId, new CoordinatorDeleteBotRequest(BotId))));
        Assert.False(await _authorizer.IsAllowedAsync(From(ManagerId, new CoordinatorBotLoginCodeRequest(HigherBotId))));
        Assert.False(await _authorizer.IsAllowedAsync(From(ManagerId, new CoordinatorDeleteBotRequest(HigherBotId))));
    }
}

/// <summary>The bot requests: codes for bots only, devices ended on request, people never deleted as bots.</summary>
public class BotHandlerTests
{
    private const string Coordinator = "coordinator";
    private const int ManagerId = 1;
    private const int BotId = 7;
    private const int PersonId = 8;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<IMessageSender> _sender = new();
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<IConnectionManager> _connections = new();
    private readonly InMemoryUserSessionRepository _devices = new();
    private readonly InMemoryUserLoginCodeRepository _codes = new();
    private readonly List<BaseMessage> _sent = [];
    private readonly UserModel _bot;

    public BotHandlerTests()
    {
        _sender.Setup(s => s.SendMessageById(It.IsAny<string>(), It.IsAny<BaseMessage>()))
            .Callback((string _, BaseMessage message) => _sent.Add(message));
        _connections.Setup(c => c.GetUserConnections(It.IsAny<int>())).Returns([]);

        _bot = UserModel.CreateBot(Coordinator, "Helper", ManagerId, isDebug: false, _time.GetUtcNow());
        _bot.Id = BotId;
        var person = UserModel.Create(Coordinator, "alice", null, _time.GetUtcNow());
        person.Id = PersonId;
        _userManager.Setup(u => u.GetUserAsync(BotId)).ReturnsAsync(_bot);
        _userManager.Setup(u => u.GetUserAsync(PersonId)).ReturnsAsync(person);
    }

    private BotLoginCodes Codes => new(Coordinator, _codes, _time);

    private static T From<T>(T request) where T : BaseMessage
    {
        request.SenderId = ManagerId;
        return request;
    }

    private T Last<T>() => Assert.IsType<T>(_sent[^1]);

    [Fact]
    public async Task CreateBot_AnswersTheBotAndACodeForADay()
    {
        _userManager.Setup(u => u.CreateBot(ManagerId, "Helper", true)).ReturnsAsync((null, _bot));
        var handler = new CreateBotHandler(NullLogger.Instance, _sender.Object, _userManager.Object, Codes);

        await handler.HandleMessage(From(new CoordinatorCreateBotRequest("Helper", IsDebug: true)), "connection");

        var response = Last<CoordinatorCreateBotResponse>();
        Assert.Equal(BotId, response.Bot.Id);
        Assert.Equal(_time.GetUtcNow() + BotLoginCodes.Lifetime, response.LoginCode.ExpiresAt);
        Assert.Equal(BotId, await _codes.TakeAsync(Coordinator, LoginCodes.Hash(response.LoginCode.Code), _time.GetUtcNow()));
    }

    [Fact]
    public async Task CreateBot_Refused_AnswersTheError()
    {
        _userManager.Setup(u => u.CreateBot(ManagerId, "Helper", false)).ReturnsAsync((ErrorCode.BotLimitReached, null));
        var handler = new CreateBotHandler(NullLogger.Instance, _sender.Object, _userManager.Object, Codes);

        await handler.HandleMessage(From(new CoordinatorCreateBotRequest("Helper")), "connection");

        Assert.Equal(ErrorCode.BotLimitReached, Last<ErrorResponse>().Code);
    }

    [Fact]
    public async Task NewCode_ReplacesTheEarlierOne()
    {
        var first = await Codes.IssueAsync(BotId);
        var handler = new BotLoginCodeHandler(NullLogger.Instance, _sender.Object, _userManager.Object,
            new UserSessions(_devices, _connections.Object, Coordinator, _time), Codes);

        await handler.HandleMessage(From(new CoordinatorBotLoginCodeRequest(BotId, EndDevices: false)), "connection");

        var second = Last<CoordinatorBotLoginCodeResponse>().LoginCode;
        Assert.Null(await _codes.TakeAsync(Coordinator, LoginCodes.Hash(first.Code), _time.GetUtcNow()));
        Assert.Equal(BotId, await _codes.TakeAsync(Coordinator, LoginCodes.Hash(second.Code), _time.GetUtcNow()));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task NewCode_EndsTheDevicesOfTheBotOnlyOnRequest(bool endDevices, int left)
    {
        var now = _time.GetUtcNow();
        await _devices.AddAsync(new UserSession(Guid.NewGuid(), Coordinator, BotId, null, null, "bot", "Linux", "1",
            now, now, now.AddDays(90), null, [1]));
        var handler = new BotLoginCodeHandler(NullLogger.Instance, _sender.Object, _userManager.Object,
            new UserSessions(_devices, _connections.Object, Coordinator, _time), Codes);

        await handler.HandleMessage(From(new CoordinatorBotLoginCodeRequest(BotId, endDevices)), "connection");

        Assert.Equal(left, _devices.All.Count);
    }

    [Fact]
    public async Task NewCode_ForAPerson_IsUserNotFound()
    {
        var handler = new BotLoginCodeHandler(NullLogger.Instance, _sender.Object, _userManager.Object,
            new UserSessions(_devices, _connections.Object, Coordinator, _time), Codes);

        await handler.HandleMessage(From(new CoordinatorBotLoginCodeRequest(PersonId)), "connection");

        Assert.Equal(ErrorCode.UserNotFound, Last<ErrorResponse>().Code);
    }

    [Fact]
    public async Task DeleteBot_DeletesTheBot()
    {
        _userManager.Setup(u => u.DeleteUser(BotId)).ReturnsAsync((ErrorCode?)null);
        var handler = new DeleteBotHandler(NullLogger.Instance, _sender.Object, _userManager.Object,
            _connections.Object);

        await handler.HandleMessage(From(new CoordinatorDeleteBotRequest(BotId)), "connection");

        Assert.Equal(BotId, Last<CoordinatorDeleteBotResponse>().TargetUserId);
        _userManager.Verify(u => u.DeleteUser(BotId));
    }

    [Fact]
    public async Task DeleteBot_OfAPerson_DeletesNoOne()
    {
        var handler = new DeleteBotHandler(NullLogger.Instance, _sender.Object, _userManager.Object,
            _connections.Object);

        await handler.HandleMessage(From(new CoordinatorDeleteBotRequest(PersonId)), "connection");

        Assert.Equal(ErrorCode.UserNotFound, Last<ErrorResponse>().Code);
        _userManager.Verify(u => u.DeleteUser(It.IsAny<int>()), Times.Never);
    }
}
