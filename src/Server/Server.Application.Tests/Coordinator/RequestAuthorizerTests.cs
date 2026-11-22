using Server.Application.Coordinator.Sessions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Moq;
using Server.Application.Coordinator.Roles;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
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
using Shared.Models.Requests.SessionActions.HybridSession;

namespace Server.Application.Tests.Coordinator;

public class RequestAuthorizerTests
{
    private const int SessionId = 5;
    private const int CreatorId = 1;
    private const int ModeratorId = 2;
    private const int PlainId = 3;
    private const int AdminId = 4;
    private const int ViewerId = 6;

    private readonly Mock<ICoordinatorPermissions> _permissions = new();
    private readonly Mock<IHybridSessionManager> _sessions = new();
    private readonly Mock<IHybridSessionPeerManager> _peers = new();
    private readonly RequestAuthorizer _authorizer;

    public RequestAuthorizerTests()
    {
        var permissions = new Dictionary<int, Permission>
        {
            [CreatorId] = Permission.None,
            [ModeratorId] = Permission.KickMembers,
            [PlainId] = Permission.None,
            [AdminId] = Permission.All,
            [ViewerId] = Permission.ViewSessions
        };
        var positions = new Dictionary<int, int>
            { [CreatorId] = 0, [ModeratorId] = 1, [PlainId] = 0, [AdminId] = 2, [ViewerId] = 0 };

        _permissions.Setup(p => p.Has(It.IsAny<int>(), It.IsAny<Permission>()))
            .Returns((int userId, Permission permission) => (permissions[userId] & permission) == permission);
        _permissions.Setup(p => p.HighestPositionOfAsync(It.IsAny<int>()))
            .ReturnsAsync((int userId) => positions[userId]);

        var session = new Mock<IHybridSession>();
        session.SetupGet(s => s.Model)
            .Returns(HybridSessionModel.Create("c", "Room", 10, WebRTCEngine.Mediasoup));
        session.SetupGet(s => s.PeerManager).Returns(_peers.Object);
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
        From(senderId, new KickFromSessionRequest(sessionId, targetId, null));

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Join_WithoutJoinSessions_OnlyIntoAPlaceKeptForAMove(bool isAwaitingMove, bool allowed)
    {
        _peers.Setup(p => p.IsAwaitingMove(ViewerId)).Returns(isAwaitingMove);

        Assert.Equal(allowed, await _authorizer.IsAllowedAsync(
            From(ViewerId, new HybridSessionJoinRequest(SessionId))));
    }

    [Fact]
    public async Task Join_IntoAKeptPlace_StillNeedsToSeeTheSession()
    {
        _peers.Setup(p => p.IsAwaitingMove(PlainId)).Returns(true);

        Assert.False(await _authorizer.IsAllowedAsync(From(PlainId, new HybridSessionJoinRequest(SessionId))));
    }

    [Fact]
    public async Task OpenRequest_IsAllowed()
    {
        Assert.True(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorInfoRequest())));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task MicrophoneMute_WithoutSpeak_CanMuteButCannotUnmute(bool isMuted, bool allowed)
    {
        Assert.Equal(allowed, await _authorizer.IsAllowedAsync(
            From(PlainId, new HybridSessionSetMicrophoneMuteRequest(SessionId, isMuted))));
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

    // Sessions have no host: creating a session gives no right in it
    [Fact]
    public async Task Kick_ByCreatorWithoutARole_IsRefused()
    {
        Assert.False(await _authorizer.IsAllowedAsync(Kick(CreatorId, PlainId)));
    }

    [Fact]
    public async Task Kick_ByPlainUser_IsRefused()
    {
        Assert.False(await _authorizer.IsAllowedAsync(Kick(PlainId, CreatorId)));
    }

    [Fact]
    public async Task Creator_IsNotAllowedWhatPlainUsersMayNotDo()
    {
        Assert.False(await _authorizer.IsAllowedAsync(From(CreatorId, new CreateSessionCategoryRequest("x"))));
    }

    [Fact]
    public async Task RenameSelf_IsOpen_RenameOthers_NeedsManageUsers()
    {
        Assert.True(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorChangeUsernameRequest("x"))));
        Assert.True(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorChangeUsernameRequest("x", PlainId))));
        Assert.False(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorChangeUsernameRequest("x", CreatorId))));
        Assert.True(await _authorizer.IsAllowedAsync(From(AdminId, new CoordinatorChangeUsernameRequest("x", PlainId))));
    }

    [Fact]
    public async Task BanOrDeleteUser_NeedsBanMembers_AndAHigherRole()
    {
        Assert.False(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorBanRequest(CreatorId, BanKind.Account))));
        Assert.True(await _authorizer.IsAllowedAsync(From(AdminId, new CoordinatorBanRequest(PlainId, BanKind.Account))));
        Assert.True(await _authorizer.IsAllowedAsync(From(AdminId, new CoordinatorDeleteUserRequest(PlainId))));
        Assert.False(await _authorizer.IsAllowedAsync(From(PlainId, new CoordinatorDeleteUserRequest(CreatorId))));
    }

    [Fact]
    public async Task BanListingAndRevocation_AreNotGrantedToPlainUsers()
    {
        Assert.False(await _authorizer.IsAllowedAsync(From(CreatorId, new CoordinatorBansRequest())));
        Assert.False(await _authorizer.IsAllowedAsync(From(CreatorId, new CoordinatorRevokeBanRequest(Guid.NewGuid()))));
        Assert.True(await _authorizer.IsAllowedAsync(From(AdminId, new CoordinatorBansRequest())));
        Assert.True(await _authorizer.IsAllowedAsync(From(AdminId, new CoordinatorRevokeBanRequest(Guid.NewGuid()))));
    }

    [Fact]
    public void FrozenUser_MayRead_ButNotWrite()
    {
        _permissions.Setup(p => p.IsFrozen(PlainId)).Returns(true);

        Assert.False(_authorizer.IsFrozenOut(From(PlainId, new ChatSessionHistoryPageRequest(1, null, 10))));
        Assert.True(_authorizer.IsFrozenOut(From(PlainId, new HybridSessionJoinRequest(1))));
        Assert.True(_authorizer.IsFrozenOut(From(PlainId, new CoordinatorChangeUsernameRequest("x"))));
        Assert.False(_authorizer.IsFrozenOut(From(CreatorId, new CoordinatorChangeUsernameRequest("x"))));
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
