using Server.Application.Coordinator.Sessions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Moq;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Core.Models.HybridSession;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Enums.WebRTC;
using Shared.Models;
using Shared.Models.Requests;
using Shared.Models.Requests.Coordinator;
using Shared.Models.Requests.SessionActions.ChatSession;
using Shared.Models.Requests.SessionActions.Generic;

namespace Server.Application.Tests.Coordinator;

public class RequestAuthorizerTests
{
    private const int SessionId = 5;
    private const int HostId = 1;
    private const int ModeratorId = 2;
    private const int PlainId = 3;
    private const int AdminId = 4;

    private readonly Mock<ICoordinatorPermissions> _permissions = new();
    private readonly Mock<IHybridSessionManager> _sessions = new();
    private readonly RequestAuthorizer _authorizer;

    public RequestAuthorizerTests()
    {
        var permissions = new Dictionary<int, Permission>
        {
            [HostId] = Permission.None,
            [ModeratorId] = Permission.KickMembers,
            [PlainId] = Permission.None,
            [AdminId] = Permission.All
        };
        var positions = new Dictionary<int, int> { [HostId] = 0, [ModeratorId] = 1, [PlainId] = 0, [AdminId] = 2 };

        _permissions.Setup(p => p.Has(It.IsAny<int>(), It.IsAny<Permission>()))
            .Returns((int userId, Permission permission) => (permissions[userId] & permission) == permission);
        _permissions.Setup(p => p.HighestPositionOfAsync(It.IsAny<int>()))
            .ReturnsAsync((int userId) => positions[userId]);

        var session = new Mock<IHybridSession>();
        session.SetupGet(s => s.Model)
            .Returns(HybridSessionModel.Create("c", "Room", 10, HostId, WebRTCEngine.Mediasoup));
        IHybridSession? found = session.Object;
        _sessions.Setup(s => s.TryGetSession(SessionId, out found)).Returns(true);

        _authorizer = new RequestAuthorizer(_permissions.Object, _sessions.Object, Mock.Of<ISessionManager>());
    }

    private static T From<T>(int senderId, T message) where T : BaseMessage
    {
        message.SenderId = senderId;
        return message;
    }

    private static KickFromSessionRequest Kick(int senderId, int targetId, int sessionId = SessionId) =>
        From(senderId, new KickFromSessionRequest(sessionId, targetId, SessionType.Hybrid, null));

    [Fact]
    public async Task OpenRequest_IsAllowed()
    {
        Assert.True(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorInfoRequest())));
    }

    [Fact]
    public async Task MissingPermission_IsRefused()
    {
        Assert.False(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorSettingsRequest())));
    }

    [Fact]
    public async Task SystemUser_IsAllowedEverything()
    {
        Assert.True(await _authorizer.IsAllowedAsync(From(UserModel.SystemUserId, new CoordinatorSettingsRequest())));
    }

    [Fact]
    public async Task Kick_ByRole_OnLowerUser_IsAllowed()
    {
        Assert.True(await _authorizer.IsAllowedAsync(Kick(ModeratorId, PlainId)));
    }

    [Fact]
    public async Task Kick_ByRole_OnHigherUser_IsRefused()
    {
        Assert.False(await _authorizer.IsAllowedAsync(Kick(ModeratorId, AdminId)));
    }

    [Fact]
    public async Task Kick_ByHost_OnUserOfTheSameRank_IsAllowed()
    {
        Assert.True(await _authorizer.IsAllowedAsync(Kick(HostId, PlainId)));
    }

    [Fact]
    public async Task Kick_ByHost_OnHigherUser_IsRefused()
    {
        Assert.False(await _authorizer.IsAllowedAsync(Kick(HostId, ModeratorId)));
    }

    [Fact]
    public async Task Kick_ByPlainUser_IsRefused()
    {
        Assert.False(await _authorizer.IsAllowedAsync(Kick(PlainId, HostId)));
    }

    [Fact]
    public async Task HostOfAnotherSession_IsNotHost()
    {
        IHybridSession? none = null;
        _sessions.Setup(s => s.TryGetSession(9, out none)).Returns(false);

        Assert.False(await _authorizer.IsAllowedAsync(Kick(HostId, PlainId, sessionId: 9)));
    }

    [Fact]
    public async Task Host_IsNotAllowedWhatHostsMayNotDo()
    {
        Assert.False(await _authorizer.IsAllowedAsync(From(HostId, new CreateSessionCategoryRequest("x"))));
    }

    [Fact]
    public async Task RenameSelf_IsOpen_RenameOthers_NeedsManageUsers()
    {
        Assert.True(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorChangeUsernameRequest("x"))));
        Assert.True(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorChangeUsernameRequest("x", PlainId))));
        Assert.False(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorChangeUsernameRequest("x", HostId))));
        Assert.True(await _authorizer.IsAllowedAsync(From(AdminId, new CoordinatorChangeUsernameRequest("x", PlainId))));
    }

    [Fact]
    public async Task BanOrDeleteUser_NeedsBanMembers_AndAHigherRole()
    {
        Assert.False(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorSetUserStateRequest(HostId, UserState.Banned))));
        Assert.True(await _authorizer.IsAllowedAsync(From(AdminId, new CoordinatorSetUserStateRequest(PlainId, UserState.Banned))));
        Assert.True(await _authorizer.IsAllowedAsync(From(AdminId, new CoordinatorDeleteUserRequest(PlainId))));
        Assert.False(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorDeleteUserRequest(HostId))));
    }

    [Fact]
    public void FrozenUser_MayRead_ButNotWrite()
    {
        _permissions.Setup(p => p.IsFrozen(PlainId)).Returns(true);

        Assert.False(_authorizer.IsFrozenOut(From(PlainId, new ChatSessionHistoryRequest(1, 10, null))));
        Assert.False(_authorizer.IsFrozenOut(From(PlainId, new JoinSessionRequest(1, SessionType.Chat))));
        Assert.True(_authorizer.IsFrozenOut(From(PlainId, new JoinSessionRequest(1, SessionType.Hybrid))));
        Assert.True(_authorizer.IsFrozenOut(From(PlainId, new CoordinatorChangeUsernameRequest("x"))));
        Assert.False(_authorizer.IsFrozenOut(From(HostId, new CoordinatorChangeUsernameRequest("x"))));
    }

    public static TheoryData<Type> RequestTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var registration in typeof(BaseMessage).GetCustomAttributes<JsonDerivedTypeAttribute>())
            if (typeof(Request).IsAssignableFrom(registration.DerivedType))
                data.Add(registration.DerivedType);
        return data;
    }

    [Theory]
    [MemberData(nameof(RequestTypes))]
    public void EveryRequest_HasAPolicy(Type requestType)
    {
        var request = (BaseMessage)RuntimeHelpers.GetUninitializedObject(requestType);

        Assert.True(RequestPolicies.Of(request) != null, $"{requestType.Name} has no entry in RequestPolicies");
    }
}
