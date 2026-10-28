using Shared.Enums;
using Shared.Models.Responses;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Security;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Handlers.Authentication;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Tests.Fakes;
using Server.Core.Models.User;
using Shared.Enums.Auth;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Auth;
using Shared.Models.Responses.Auth;

namespace Server.Application.Tests.Coordinator;

public class TwoStageLoginTests
{
    private const string ConnectionId = "connection";
    private const string Identity = "identity";
    private const int UserId = 1;

    private readonly Mock<IAuthenticator> _authenticator = new();
    private readonly Mock<IUserManager> _users = new();
    private readonly Mock<IConnectionManager> _connections = new();
    private readonly Mock<IClientConnection> _connection = new();
    private readonly Mock<IMessageSender> _sender = new();
    private readonly List<BaseMessage> _sent = [];
    private readonly InMemoryUserSessionRepository _sessions = new();
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly AuthenticationActionHandler _firstStage;
    private readonly TwoFactorAuthenticationHandler _codes;
    private readonly Fido2AuthenticationHandler _passkeys;

    private static readonly PasskeyLoginOptions PasskeyOptions =
        new("{options}", "challenge", "localhost", 60000, ["AQ"], "required");

    public TwoStageLoginTests()
    {
        _connection.SetupProperty(c => c.UserId);
        _connection.Setup(c => c.ConnectionId).Returns(ConnectionId);
        var connection = _connection.Object;
        _connections.Setup(c => c.TryGetConnectionById(ConnectionId, out connection)).Returns(true);
        _sender.Setup(s => s.SendMessageById(ConnectionId, It.IsAny<BaseMessage>()))
            .Callback((string _, BaseMessage message) => _sent.Add(message));

        var user = new UserModel(UserId, "coordinator", "user", UserState.Normal, UserStatus.Online, DateTime.UtcNow,
            DateTime.UtcNow);
        _users.Setup(u => u.AttachUser(It.IsAny<string?>(), It.IsAny<IClientConnection>()))
            .Callback((string? _, IClientConnection attached) => attached.UserId = UserId)
            .ReturnsAsync(new UserAttachment(AttachUserResult.Attached, user, Identity));
        _authenticator.Setup(a => a.CheckLoginData(It.IsAny<string?>())).Returns(true);

        _users.Setup(u => u.AttachUser(It.IsAny<int>(), It.IsAny<IClientConnection>()))
            .Callback((int id, IClientConnection attached) => attached.UserId = id)
            .ReturnsAsync(new UserAttachment(AttachUserResult.Attached, user));
        _connection.SetupProperty(c => c.UserSessionId);
        _connection.Setup(c => c.IpEndpoint).Returns("10.0.0.1");

        var userLogin = new UserLogin(_users.Object,
            new UserSessions(_sessions, _connections.Object, "coordinator", _time));
        var pending = new PendingLogins();
        var secondStage = new SecondStageAuthentication(NullLogger.Instance, _sender.Object, _connections.Object,
            userLogin, pending);
        _firstStage = new AuthenticationActionHandler(NullLogger.Instance, _sender.Object, _authenticator.Object,
            _connections.Object, userLogin, pending);
        _codes = new TwoFactorAuthenticationHandler(_authenticator.Object, secondStage);
        _passkeys = new Fido2AuthenticationHandler(_authenticator.Object, secondStage);
    }

    private void RequireSecondStage(bool acceptsCodes = true, bool passkey = false)
    {
        var methods = new List<SecondStageAuthType>();
        if (acceptsCodes)
            methods.Add(SecondStageAuthType.TwoFactor);
        if (passkey)
            methods.Add(SecondStageAuthType.FIDO2);
        _authenticator.Setup(a => a.BeginUserLoginAsync(Identity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginChallenge(UserId, acceptsCodes, passkey ? PasskeyOptions : null, methods));
    }

    private Task Login() => _firstStage.HandleMessage(new UserAuthRequest(null, Identity), ConnectionId);

    private Task SendCode(string code) =>
        _codes.HandleMessage(new TwoFactorUserAuthRequest(null, code), ConnectionId);

    private static FIDO2AssertionDTO Assertion() => new()
    {
        CredentialId = "AQ", ClientDataJson = "Ag", AuthenticatorData = "Aw", Signature = "BA"
    };

    [Fact]
    public async Task UserWithoutFactors_IsLetInAtOnce()
    {
        await Login();

        var response = Assert.IsType<UserAuthResponse>(Assert.Single(_sent));
        Assert.Equal(AuthResult.Success, response.AuthResult);
        Assert.Equal(UserId, _connection.Object.UserId);
    }

    [Fact]
    public async Task UserWithFactors_IsAskedForTheSecondStage_AndNotAttached()
    {
        RequireSecondStage(passkey: true);

        await Login();

        var response = Assert.IsType<UserAuthResponse>(Assert.Single(_sent));
        Assert.Equal(AuthResult.SecondStageRequired, response.AuthResult);
        Assert.Null(response.User);
        Assert.Equal([SecondStageAuthType.TwoFactor, SecondStageAuthType.FIDO2], response.SecondStages);
        Assert.Equal("challenge", response.FIDO2LoginOptions!.Challenge);
        Assert.Equal("AQ", Assert.Single(response.FIDO2LoginOptions.AllowedCredentials).Id);
        _users.Verify(u => u.AttachUser(It.IsAny<string?>(), It.IsAny<IClientConnection>()), Times.Never);
        _connection.Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()), Times.Never);
    }

    [Fact]
    public async Task RightCode_LetsTheUserIn()
    {
        RequireSecondStage();
        _authenticator.Setup(a => a.CheckUserLoginData(UserId, null, "123456")).ReturnsAsync(true);
        await Login();

        await SendCode("123456");

        var response = Assert.IsType<TwoFactorUserAuthResponse>(_sent[^1]);
        Assert.Equal(UserId, response.User!.Id);
        Assert.Equal(UserId, _connection.Object.UserId);
        _users.Verify(u => u.AttachUser(Identity, _connection.Object), Times.Once);
    }

    [Fact]
    public async Task LoginWithTheIdentity_StartsASession_AndGivesItsToken()
    {
        await _firstStage.HandleMessage(new UserAuthRequest(null, Identity,
            Device: new DeviceInfoDTO("desk", "Linux", "0.8.0")), ConnectionId);

        var response = Assert.IsType<UserAuthResponse>(Assert.Single(_sent));
        var session = Assert.Single(_sessions.All);
        Assert.False(string.IsNullOrEmpty(response.SessionToken));
        Assert.Equal(SecretHash.Of(response.SessionToken!), session.TokenHash);
        Assert.Equal(("desk", "Linux", "0.8.0"), (session.DeviceName, session.DevicePlatform, session.AppVersion));
        Assert.Equal(session.Id, _connection.Object.UserSessionId);
    }

    [Fact]
    public async Task SecondStage_GivesTheTokenOfANewSession()
    {
        RequireSecondStage();
        _authenticator.Setup(a => a.CheckUserLoginData(UserId, null, "123456")).ReturnsAsync(true);
        await Login();

        await SendCode("123456");

        var response = Assert.IsType<TwoFactorUserAuthResponse>(_sent[^1]);
        Assert.Equal(SecretHash.Of(response.SessionToken!), Assert.Single(_sessions.All).TokenHash);
    }

    [Fact]
    public async Task Token_LetsTheDeviceIn_WithoutTheSecondStage_AndIsReplaced()
    {
        var token = await StartSession();
        RequireSecondStage(passkey: true);

        await _firstStage.HandleMessage(new UserAuthRequest(null, Identity, SessionToken: token), ConnectionId);

        var response = Assert.IsType<UserAuthResponse>(_sent[^1]);
        Assert.Equal(AuthResult.Success, response.AuthResult);
        Assert.NotEqual(token, response.SessionToken);
        Assert.Null(response.UserIdentity);
        var session = Assert.Single(_sessions.All);
        Assert.Equal(SecretHash.Of(response.SessionToken!), session.TokenHash);
        Assert.Equal(SecretHash.Of(token), session.PreviousTokenHash);
        _users.Verify(u => u.AttachUser(UserId, _connection.Object), Times.Once);
    }

    [Fact]
    public async Task PreviousToken_WorksUntilTheNextOneIsUsed()
    {
        var first = await StartSession();
        await LoginWithToken(first); // the answer with the next token never reached the device
        var third = await LoginWithToken(first);
        Assert.Equal(AuthResult.Success, Assert.IsType<UserAuthResponse>(_sent[^1]).AuthResult);

        await LoginWithToken(third);
        RequireSecondStage();
        await _firstStage.HandleMessage(new UserAuthRequest(null, Identity, SessionToken: first), ConnectionId);

        Assert.Equal(AuthResult.SecondStageRequired, Assert.IsType<UserAuthResponse>(_sent[^1]).AuthResult);
    }

    [Fact]
    public async Task ExpiredToken_FallsBackToTheIdentity()
    {
        var token = await StartSession();
        _time.Advance(UserSessions.Lifetime + TimeSpan.FromMinutes(1));
        RequireSecondStage();

        await _firstStage.HandleMessage(new UserAuthRequest(null, Identity, SessionToken: token), ConnectionId);

        Assert.Equal(AuthResult.SecondStageRequired, Assert.IsType<UserAuthResponse>(_sent[^1]).AuthResult);
    }

    [Fact]
    public async Task UsedToken_KeepsTheSessionAlive()
    {
        var token = await StartSession();
        _time.Advance(UserSessions.Lifetime - TimeSpan.FromDays(1));
        token = await LoginWithToken(token);
        _time.Advance(TimeSpan.FromDays(2));

        await LoginWithToken(token);

        Assert.Equal(AuthResult.Success, Assert.IsType<UserAuthResponse>(_sent[^1]).AuthResult);
    }

    /// <summary>Logs in with the identity, as a fresh connection; returns the token of the session.</summary>
    private async Task<string> StartSession()
    {
        await Login();
        _connection.Object.UserId = null;
        return Assert.IsType<UserAuthResponse>(_sent[^1]).SessionToken!;
    }

    private async Task<string> LoginWithToken(string token)
    {
        await _firstStage.HandleMessage(new UserAuthRequest(null, Identity, SessionToken: token), ConnectionId);
        _connection.Object.UserId = null;
        return Assert.IsType<UserAuthResponse>(_sent[^1]).SessionToken!;
    }

    [Fact]
    public async Task WrongCodes_AreRetried_ThenTheConnectionIsClosed()
    {
        RequireSecondStage();
        await Login();

        for (var i = 1; i < PendingLogins.MaxFailures; i++)
        {
            await SendCode("000000");
            Assert.Equal(ErrorCode.AuthFailed, Assert.IsType<ErrorResponse>(_sent[^1]).Code);
            _connection.Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()), Times.Never);
        }

        await SendCode("000000");

        _connection.Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()), Times.Once);
        _users.Verify(u => u.AttachUser(It.IsAny<string?>(), It.IsAny<IClientConnection>()), Times.Never);
    }

    [Fact]
    public async Task SecondStage_WithoutAFirst_ClosesTheConnection()
    {
        _authenticator.Setup(a => a.CheckUserLoginData(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        await SendCode("123456");

        Assert.Equal(ErrorCode.AuthFailed, Assert.IsType<ErrorResponse>(Assert.Single(_sent)).Code);
        _connection.Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()), Times.Once);
        _users.Verify(u => u.AttachUser(It.IsAny<string?>(), It.IsAny<IClientConnection>()), Times.Never);
    }

    [Fact]
    public async Task Codes_ForAUserWithPasskeysAlone_AreNotChecked()
    {
        RequireSecondStage(acceptsCodes: false, passkey: true);
        await Login();

        await SendCode("123456");

        Assert.Equal(ErrorCode.AuthFailed, Assert.IsType<ErrorResponse>(_sent[^1]).Code);
        _authenticator.Verify(a => a.CheckUserLoginData(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Passkey_ThatHolds_LetsTheUserIn()
    {
        RequireSecondStage(acceptsCodes: false, passkey: true);
        _authenticator.Setup(a => a.CheckPasskeyAsync(UserId, "{options}",
                It.Is<PasskeyAssertion>(p => p.CredentialId.SequenceEqual(new byte[] { 1 }))))
            .ReturnsAsync(true);
        await Login();

        await _passkeys.HandleMessage(new FIDO2UserAuthRequest(Assertion()), ConnectionId);

        Assert.IsType<FIDO2UserAuthResponse>(_sent[^1]);
        Assert.Equal(UserId, _connection.Object.UserId);
    }

    [Fact]
    public async Task Passkey_ForAUserWithoutPasskeys_IsRefused()
    {
        RequireSecondStage();
        await Login();

        await _passkeys.HandleMessage(new FIDO2UserAuthRequest(Assertion()), ConnectionId);

        Assert.Equal(ErrorCode.AuthFailed, Assert.IsType<ErrorResponse>(_sent[^1]).Code);
        _authenticator.Verify(a => a.CheckPasskeyAsync(It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<PasskeyAssertion>()), Times.Never);
    }

    [Fact]
    public async Task BannedMeanwhile_IsRefusedAfterTheSecondStage()
    {
        RequireSecondStage();
        _authenticator.Setup(a => a.CheckUserLoginData(UserId, null, "123456")).ReturnsAsync(true);
        _users.Setup(u => u.AttachUser(It.IsAny<string?>(), It.IsAny<IClientConnection>()))
            .ReturnsAsync(UserAttachment.Rejected(AttachUserResult.Banned));
        await Login();

        await SendCode("123456");

        Assert.Equal(ErrorCode.Banned, Assert.IsType<ErrorResponse>(_sent[^1]).Code);
        _connection.Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()), Times.Once);
    }

    [Fact]
    public async Task SecondStage_OnALoggedInConnection_DoesNotCloseIt()
    {
        await Login();

        await SendCode("123456");

        Assert.Equal(ErrorCode.AuthFailed, Assert.IsType<ErrorResponse>(_sent[^1]).Code);
        _connection.Verify(c => c.Close(It.IsAny<ConnectionCloseReason>()), Times.Never);
    }

    [Theory]
    [InlineData("AQ", "AQ==")]
    [InlineData("+/8", "-_8")]
    public void Assertion_TakesBase64UrlAndPlainBase64(string plain, string url)
    {
        var a = Fido2AuthenticationHandler.TryDecode(new FIDO2AssertionDTO
            { CredentialId = plain, ClientDataJson = url, AuthenticatorData = "AQ", Signature = "AQ" });

        Assert.NotNull(a);
        Assert.Equal(a.CredentialId, a.ClientDataJson);
    }

    [Fact]
    public void Assertion_WithAMissingOrBrokenPart_IsNotDecoded()
    {
        Assert.Null(Fido2AuthenticationHandler.TryDecode(new FIDO2AssertionDTO
            { CredentialId = "AQ", ClientDataJson = "", AuthenticatorData = "AQ", Signature = "AQ" }));
        Assert.Null(Fido2AuthenticationHandler.TryDecode(new FIDO2AssertionDTO
            { CredentialId = "AQ", ClientDataJson = "AQ", AuthenticatorData = "AQ", Signature = "*!" }));
    }
}

public class PendingLoginsTests
{
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider _time = new();
    private readonly PendingLogins _logins;
    private static readonly LoginChallenge Challenge = new(1, true, null, []);

    public PendingLoginsTests() => _logins = new PendingLogins(_time);

    [Fact]
    public void Login_ExpiresAfterItsLifetime()
    {
        _logins.Start("c", "identity", null, Challenge);
        _time.Advance(PendingLogins.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(_logins.Get("c"));
    }

    [Fact]
    public void OldLogins_AreSweptWhenANewOneStarts()
    {
        _logins.Start("old", "identity", null, Challenge);
        _time.Advance(PendingLogins.Lifetime + TimeSpan.FromSeconds(1));

        _logins.Start("new", "identity", null, Challenge);

        Assert.Equal(1, _logins.Count);
    }
}
