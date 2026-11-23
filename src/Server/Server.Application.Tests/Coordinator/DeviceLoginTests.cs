using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Security;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Handlers.Authentication;
using Server.Application.Coordinator.Handlers.Users.Devices;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Tests.Fakes;
using Server.Core.Models.User;
using Shared.Auth;
using Shared.Enums;
using Shared.Enums.Auth;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.DataTransferObjects;
using Shared.Models.Notifications.CoordinatorInfo;
using Shared.Models.Requests.Auth;
using Shared.Models.Requests.Coordinator.Devices;
using Shared.Models.Responses;
using Shared.Models.Responses.Auth;
using Shared.Models.Responses.Coordinator.Devices;

namespace Server.Application.Tests.Coordinator;

public class DeviceLoginTests
{
    private const string Coordinator = "coordinator";
    private const string ConnectionId = "new";
    private const int UserId = 1;

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly InMemoryUserSessionRepository _devices = new();
    private readonly InMemoryUserLoginCodeRepository _codes = new();
    private readonly Mock<IAuthenticator> _authenticator = new();
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IConnectionManager> _connections = new();
    private readonly Mock<IMessageSender> _sender = new();
    private readonly Dictionary<string, Mock<IClientConnection>> _connected = [];
    private readonly List<(string ConnectionId, BaseMessage Message)> _sent = [];
    private readonly LoginNonces _nonces;
    private readonly DeviceLinks _links;
    private readonly UserLogin _login;
    private readonly LoginChallengeHandler _challenge;
    private readonly LoginHandler _handler;
    private readonly LoginProofHandler _proof;
    private readonly UserModel _user = new(UserId, Coordinator, "Alice", UserState.Normal, UserStatus.Online,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public DeviceLoginTests()
    {
        _nonces = new LoginNonces(_time);
        _links = new DeviceLinks(_time);
        Connect(ConnectionId);

        _sender.Setup(s => s.SendMessageById(It.IsAny<string>(), It.IsAny<BaseMessage>()))
            .Callback((string id, BaseMessage message) => _sent.Add((id, message)));
        _connections.Setup(c => c.GetUserConnections(UserId))
            .Returns(() => _connected.Values.Select(c => c.Object).Where(c => c.UserId == UserId).ToList());
        _authenticator.Setup(a => a.CheckLoginData(It.IsAny<string?>())).Returns(true);
        _userManager.Setup(u => u.AttachUser(It.IsAny<int>(), It.IsAny<IClientConnection>()))
            .Callback((int id, IClientConnection connection) => connection.UserId = id)
            .ReturnsAsync(new UserAttachment(AttachUserResult.Attached, _user));
        _userManager.Setup(u => u.RegisterUser(It.IsAny<UserRecoveryKeys>(), It.IsAny<IClientConnection>()))
            .Callback((UserRecoveryKeys _, IClientConnection connection) => connection.UserId = UserId)
            .ReturnsAsync(new UserAttachment(AttachUserResult.Attached, _user));

        var replies = new LoginReplies(_sender.Object, _connections.Object);
        _login = new UserLogin(_userManager.Object, new UserSessions(_devices, _connections.Object, Coordinator, _time));
        var pending = new PendingLogins(_time);
        _challenge = new LoginChallengeHandler(replies, _nonces, Coordinator);
        _handler = new LoginHandler(NullLogger.Instance, replies, _authenticator.Object, _login, _nonces, pending,
            _users.Object, _codes, Coordinator, _time);
        _proof = new LoginProofHandler(_authenticator.Object,
            new SecondStageAuthentication(NullLogger.Instance, replies, _login, pending, _users.Object));
    }

    private Mock<IClientConnection> Connect(string id)
    {
        var connection = new Mock<IClientConnection>();
        connection.SetupProperty(c => c.UserId);
        connection.SetupProperty(c => c.UserSessionId);
        connection.SetupProperty(c => c.Device);
        connection.SetupProperty(c => c.WaitsForDeviceLinkUntil);
        connection.Setup(c => c.ConnectionId).Returns(id);
        connection.Setup(c => c.IpEndpoint).Returns("10.0.0.1");
        connection.Setup(c => c.Send(It.IsAny<BaseMessage>()))
            .Callback((BaseMessage message) => _sent.Add((id, message)));
        var value = connection.Object;
        _connections.Setup(c => c.TryGetConnectionById(id, out value)).Returns(true);
        _connected[id] = connection;
        return connection;
    }

    private static ECDsa NewKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static string PublicKey(ECDsa key) => LoginSignature.Encode(key.ExportSubjectPublicKeyInfo());

    private static string Sign(ECDsa key, byte[] payload) => LoginSignature.Encode(key.SignData(payload,
        HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    private static RecoveryKeysDTO Keys(ECDsa recovery) => new(PublicKey(recovery), PublicKey(NewKey()));

    private async Task<string> ChallengeAsync(string connectionId = ConnectionId)
    {
        await _challenge.HandleMessage(new LoginChallengeRequest(), connectionId);
        return Assert.IsType<LoginChallengeResponse>(_sent[^1].Message).Nonce;
    }

    private Task LoginAsync(LoginMethodDTO method, string connectionId = ConnectionId) =>
        _handler.HandleMessage(new LoginRequest("pw", method, new DeviceInfoDTO("desk", "Linux", "0.8.0")), connectionId);

    private T Last<T>() => Assert.IsType<T>(_sent[^1].Message);

    private async Task<(Guid DeviceId, ECDsa Key)> RegisterAsync()
    {
        var key = NewKey();
        await LoginAsync(new RegisterLoginDTO(new NewDeviceDTO(PublicKey(key)), Keys(NewKey())));
        var deviceId = Last<LoginResponse>().DeviceId!.Value;
        _connected[ConnectionId].Object.UserId = null;
        return (deviceId, key);
    }

    [Fact]
    public async Task Register_MakesTheAccountAndItsFirstDevice()
    {
        var (deviceId, _) = await RegisterAsync();

        var device = Assert.Single(_devices.All);
        Assert.Equal(deviceId, device.Id);
        Assert.Equal(DeviceLoginMethod.Registration, device.AddedBy);
        Assert.NotNull(device.PublicKey);
        Assert.Null(device.TokenHash);
        _userManager.Verify(u => u.RegisterUser(It.IsAny<UserRecoveryKeys>(), It.IsAny<IClientConnection>()), Times.Once);
    }

    [Fact]
    public async Task Device_SigningTheChallenge_LogsIn()
    {
        var (deviceId, key) = await RegisterAsync();
        var nonce = await ChallengeAsync();

        await LoginAsync(new DeviceLoginDTO(deviceId,
            Sign(key, LoginSignature.Payload(LoginSignature.DevicePurpose, Coordinator, nonce))));

        var response = Last<LoginResponse>();
        Assert.Equal(AuthResult.Success, response.AuthResult);
        Assert.Equal(deviceId, response.DeviceId);
        Assert.Equal(deviceId, _connected[ConnectionId].Object.UserSessionId);
    }

    [Fact]
    public async Task Device_WithAWrongSignature_IsRefusedAndClosed()
    {
        var (deviceId, _) = await RegisterAsync();
        var nonce = await ChallengeAsync();

        await LoginAsync(new DeviceLoginDTO(deviceId,
            Sign(NewKey(), LoginSignature.Payload(LoginSignature.DevicePurpose, Coordinator, nonce))));

        Assert.Equal(ErrorCode.AuthFailed, Last<ErrorResponse>().Code);
        _connected[ConnectionId].Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()), Times.Once);
    }

    [Fact]
    public async Task Device_ReplayingASignature_IsRefused()
    {
        var (deviceId, key) = await RegisterAsync();
        var nonce = await ChallengeAsync();
        var method = new DeviceLoginDTO(deviceId,
            Sign(key, LoginSignature.Payload(LoginSignature.DevicePurpose, Coordinator, nonce)));
        await LoginAsync(method);
        _connected[ConnectionId].Object.UserId = null;

        // The nonce was used up by the first login
        await LoginAsync(method);

        Assert.Equal(ErrorCode.AuthFailed, Last<ErrorResponse>().Code);
    }

    [Fact]
    public async Task RemovedDevice_IsToldItIsNotFound()
    {
        var (deviceId, key) = await RegisterAsync();
        await _devices.DeleteAsync(Coordinator, UserId, deviceId);
        var nonce = await ChallengeAsync();

        await LoginAsync(new DeviceLoginDTO(deviceId,
            Sign(key, LoginSignature.Payload(LoginSignature.DevicePurpose, Coordinator, nonce))));

        Assert.Equal(ErrorCode.DeviceNotFound, Last<ErrorResponse>().Code);
    }

    [Fact]
    public async Task RecoveryKey_AddsADevice_AndTellsTheOtherDevices()
    {
        var recovery = NewKey();
        var hash = SecretHash.Of(recovery.ExportSubjectPublicKeyInfo());
        _users.Setup(u => u.GetByRecoveryKeyHashAsync(Coordinator, It.Is<byte[]>(h => h.SequenceEqual(hash)),
            It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        var other = Connect("other");
        other.Object.UserId = UserId;
        var newKey = PublicKey(NewKey());
        var nonce = await ChallengeAsync();

        await LoginAsync(new RecoveryLoginDTO(new NewDeviceDTO(newKey), PublicKey(recovery),
            Sign(recovery, LoginSignature.Payload(LoginSignature.RecoveryPurpose, Coordinator, nonce, newKey))));

        Assert.Equal(AuthResult.Success, Last<LoginResponse>().AuthResult);
        Assert.Equal(DeviceLoginMethod.Recovery, Assert.Single(_devices.All).AddedBy);
        var added = Assert.IsType<AccountDeviceAddedNotification>(Assert.Single(_sent, s => s.ConnectionId == "other").Message);
        Assert.Equal(DeviceLoginMethod.Recovery, added.Device.AddedBy);
    }

    [Fact]
    public async Task RecoveryKey_SigningAnotherNewDeviceKey_IsRefused()
    {
        var recovery = NewKey();
        _users.Setup(u => u.GetByRecoveryKeyHashAsync(Coordinator, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_user);
        var nonce = await ChallengeAsync();

        await LoginAsync(new RecoveryLoginDTO(new NewDeviceDTO(PublicKey(NewKey())), PublicKey(recovery),
            Sign(recovery, LoginSignature.Payload(LoginSignature.RecoveryPurpose, Coordinator, nonce,
                PublicKey(NewKey())))));

        Assert.Equal(ErrorCode.AuthFailed, Last<ErrorResponse>().Code);
        Assert.Empty(_devices.All);
    }

    [Fact]
    public async Task Username_AsksTheFactors_AndTheRightProofAddsTheDevice()
    {
        _users.Setup(u => u.GetByUsernameAsync(Coordinator, "alice", It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _authenticator.Setup(a => a.BeginFactorLoginAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginChallenge(UserId, true, null, [LoginFactor.Password]));
        _authenticator.Setup(a => a.CheckUserLoginData(UserId, "secret", null)).ReturnsAsync(true);

        await LoginAsync(new UsernameLoginDTO(new NewDeviceDTO(PublicKey(NewKey())), "alice"));
        var second = Last<LoginResponse>();
        Assert.Equal(AuthResult.SecondStageRequired, second.AuthResult);
        Assert.Equal([LoginFactor.Password], second.Factors);
        Assert.Null(_connected[ConnectionId].Object.UserId);

        await _proof.HandleMessage(new LoginProofRequest("secret", null), ConnectionId);

        Assert.Equal(AuthResult.Success, Last<LoginResponse>().AuthResult);
        Assert.Equal(DeviceLoginMethod.Factors, Assert.Single(_devices.All).AddedBy);
    }

    [Fact]
    public async Task Username_OfAnAccountWithoutFactors_IsToldSo()
    {
        _users.Setup(u => u.GetByUsernameAsync(Coordinator, "alice", It.IsAny<CancellationToken>())).ReturnsAsync(_user);

        await LoginAsync(new UsernameLoginDTO(new NewDeviceDTO(PublicKey(NewKey())), "alice"));

        Assert.Equal(ErrorCode.LoginFactorsMissing, Last<ErrorResponse>().Code);
        Assert.Empty(_devices.All);
    }

    [Fact]
    public async Task LoginCode_AddsADevice_Once()
    {
        await _codes.AddAsync(Coordinator, UserId, LoginCodes.Hash("ABCD-EFGH-JKMN"), _time.GetUtcNow(),
            _time.GetUtcNow() + LoginCodes.Lifetime);

        await LoginAsync(new LoginCodeLoginDTO(new NewDeviceDTO(PublicKey(NewKey())), "abcd efgh jkmn"));
        Assert.Equal(DeviceLoginMethod.LoginCode, Assert.Single(_devices.All).AddedBy);
        _connected[ConnectionId].Object.UserId = null;

        await LoginAsync(new LoginCodeLoginDTO(new NewDeviceDTO(PublicKey(NewKey())), "ABCD-EFGH-JKMN"));
        Assert.Equal(ErrorCode.AuthFailed, Last<ErrorResponse>().Code);
    }

    [Fact]
    public async Task LegacyIdentity_WithItsLiveToken_MovesTheAccountOver()
    {
        var now = _time.GetUtcNow();
        await _devices.AddAsync(new UserSession(Guid.NewGuid(), Coordinator, UserId, SecretHash.Of("token"), null,
            "old", "Linux", "0.7.0", now, now, now + UserSessions.Lifetime, null));
        _users.Setup(u => u.GetByIdentityAsync(Coordinator, "#identity", It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _authenticator.Setup(a => a.BeginFactorLoginAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginChallenge(UserId, true, null, [LoginFactor.Password]));

        await LoginAsync(new LegacyLoginDTO(new NewDeviceDTO(PublicKey(NewKey())), "#identity", "token", Keys(NewKey())));

        Assert.Equal(AuthResult.Success, Last<LoginResponse>().AuthResult);
        var device = Assert.Single(_devices.All);
        Assert.Equal(DeviceLoginMethod.Legacy, device.AddedBy);
        Assert.NotNull(device.PublicKey);
        _users.Verify(u => u.SetRecoveryKeysAsync(UserId, It.IsAny<UserRecoveryKeys>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task LegacyIdentity_WithoutAToken_AsksTheFactorsFirst()
    {
        _users.Setup(u => u.GetByIdentityAsync(Coordinator, "#identity", It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _authenticator.Setup(a => a.BeginFactorLoginAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginChallenge(UserId, true, null, [LoginFactor.TwoFactor]));
        _authenticator.Setup(a => a.CheckUserLoginData(UserId, null, "123456")).ReturnsAsync(true);

        await LoginAsync(new LegacyLoginDTO(new NewDeviceDTO(PublicKey(NewKey())), "#identity", null, Keys(NewKey())));
        Assert.Equal(AuthResult.SecondStageRequired, Last<LoginResponse>().AuthResult);
        _users.Verify(u => u.SetRecoveryKeysAsync(It.IsAny<int>(), It.IsAny<UserRecoveryKeys>(),
            It.IsAny<CancellationToken>()), Times.Never);

        await _proof.HandleMessage(new LoginProofRequest(null, "123456"), ConnectionId);

        Assert.Equal(AuthResult.Success, Last<LoginResponse>().AuthResult);
        _users.Verify(u => u.SetRecoveryKeysAsync(UserId, It.IsAny<UserRecoveryKeys>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task WrongServerPassword_IsRefused()
    {
        _authenticator.Setup(a => a.CheckLoginData(It.IsAny<string?>())).Returns(false);

        await LoginAsync(new RegisterLoginDTO(new NewDeviceDTO(PublicKey(NewKey())), Keys(NewKey())));

        Assert.Equal(ErrorCode.AuthFailed, Last<ErrorResponse>().Code);
        Assert.Empty(_devices.All);
    }

    [Fact]
    public async Task DeviceLink_Approved_LogsTheWaitingDeviceIn()
    {
        var replies = new LoginReplies(_sender.Object, _connections.Object);
        var start = new DeviceLinkStartHandler(replies, _authenticator.Object, _login, _links);
        var approve = new DeviceLinkApproveHandler(_sender.Object, _connections.Object, _links, _login);
        var owner = Connect("owner");
        owner.Object.UserId = UserId;

        await start.HandleMessage(new DeviceLinkStartRequest("pw", new NewDeviceDTO(PublicKey(NewKey()))), ConnectionId);
        var code = Last<DeviceLinkStartResponse>().Code;
        Assert.NotNull(_connected[ConnectionId].Object.WaitsForDeviceLinkUntil);

        await approve.HandleMessage(new DeviceLinkApproveRequest("flowrate-link:" + code, true) { SenderId = UserId },
            "owner");

        Assert.IsType<DeviceLinkApproveResponse>(_sent[^1].Message);
        var completed = Assert.IsType<DeviceLinkCompletedNotification>(
            _sent.Last(s => s.ConnectionId == ConnectionId).Message);
        Assert.Equal(DeviceLoginMethod.DeviceLink, Assert.Single(_devices.All).AddedBy);
        Assert.Equal(completed.DeviceId, _devices.All.Single().Id);
        Assert.Null(_connected[ConnectionId].Object.WaitsForDeviceLinkUntil);
    }

    [Fact]
    public async Task Logout_RemovesTheCurrentDevice()
    {
        var (deviceId, _) = await RegisterAsync();
        var connection = _connected[ConnectionId].Object;
        connection.UserId = UserId;
        connection.UserSessionId = deviceId;
        var logout = new LogoutHandler(_sender.Object, _connections.Object, _login.Devices);

        await logout.HandleMessage(new LogoutRequest { SenderId = UserId }, ConnectionId);

        Assert.Empty(_devices.All);
        Assert.Contains(_sent, s => s.Message is LogoutResponse);
        Assert.Contains(_sent, s => s.Message is AccountDeviceEndedNotification { Reason: DeviceEndReason.LoggedOut });
        _connected[ConnectionId].Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Revoke_OfAnotherDevice_DisconnectsIt()
    {
        var (first, _) = await RegisterAsync();
        var (second, _) = await RegisterAsync();
        var current = _connected[ConnectionId].Object;
        current.UserId = UserId;
        current.UserSessionId = first;
        var phone = Connect("phone");
        phone.Object.UserId = UserId;
        phone.Object.UserSessionId = second;
        var revoke = new AccountDeviceRevokeHandler(_sender.Object, _connections.Object, _login.Devices);

        await revoke.HandleMessage(new AccountDeviceRevokeRequest(second) { SenderId = UserId }, ConnectionId);

        var list = Last<AccountDevicesResponse>();
        Assert.Equal(first, Assert.Single(list.Devices).Id);
        phone.Verify(c => c.Send(It.Is<AccountDeviceEndedNotification>(n => n.Reason == DeviceEndReason.Revoked)));
        phone.Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()));
    }
}
