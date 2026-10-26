using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Security;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Authentication;
using Server.Application.Tests.Fakes;
using Server.Core.Models.Coordinator;
using Server.Core.Models.User;
using Shared.Enums.Auth;
using Shared.Enums.User;

namespace Server.Application.Tests.Coordinator;

public class AuthenticatorTests
{
    private const string Identity = "identity";
    private const int UserId = 1;

    private readonly Mock<IUserRepository> _users = new();
    private readonly InMemoryUserSecurityDataRepository _security = new();
    private readonly InMemoryUserPasskeyRepository _passkeys = new();
    private readonly Mock<IPasskeyService> _passkeyService = new();
    private readonly Mock<ITwoFactorService> _codes = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Authenticator _authenticator;
    private UserState _state = UserState.Normal;

    public AuthenticatorTests()
    {
        var model = CoordinatorInstanceModel.Create("coordinator", "127.0.0.1", 5000, DateTime.UtcNow);
        _users.Setup(u => u.GetByIdentityAsync(model.Id, Identity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new UserModel(UserId, model.Id, "user", _state, UserStatus.Online, DateTime.UtcNow,
                DateTime.UtcNow));
        _hasher.Setup(h => h.VerifyPassword("hash", "secret")).Returns(true);
        _passkeyService.Setup(p => p.IsAvailable).Returns(true);
        _passkeyService.Setup(p => p.BeginLogin(It.IsAny<IReadOnlyList<byte[]>>()))
            .Returns(new PasskeyLoginOptions("{}", "challenge", "localhost", 60000, ["AQ"], "required"));

        _authenticator = new Authenticator(NullLogger.Instance, _hasher.Object, _codes.Object,
            _passkeyService.Object, _users.Object, _security, _passkeys,
            new CoordinatorInstanceContext(model, "1.0"));
    }

    private void Security(Action<UserSecurityData> change)
    {
        var data = _security.Saved(UserId) ?? UserSecurityData.CreateEmpty(UserId);
        change(data);
        _security.UpdateAsync(data);
    }

    private void EnableCodes(long step = 100)
    {
        Security(data =>
        {
            data.OfferTwoFactorKey("KEY");
            data.EnableTwoFactor();
        });
        _codes.Setup(c => c.ValidateCode("KEY", "123456", out step)).Returns(true);
    }

    private UserPasskey AddPasskey(bool enabled = true)
    {
        var passkey = UserPasskey.Create(UserId, [1], [2], 0, null, Guid.Empty, DateTime.UtcNow);
        if (!enabled)
            passkey.Disable();
        _passkeys.AddAsync(passkey);
        Security(data => data.SetPasskeysEnabled(true));
        return passkey;
    }

    [Fact]
    public async Task UserWithoutFactors_NeedsNoSecondStage()
    {
        Security(_ => { });

        Assert.Null(await _authenticator.BeginUserLoginAsync(Identity));
    }

    [Fact]
    public async Task UnknownIdentity_NeedsNoSecondStage()
    {
        Assert.Null(await _authenticator.BeginUserLoginAsync("someone else"));
    }

    [Fact]
    public async Task BannedUser_IsLeftToBeRefusedWhenAttached()
    {
        Security(data => data.SetPassword("hash"));
        _state = UserState.Banned;

        Assert.Null(await _authenticator.BeginUserLoginAsync(Identity));
    }

    [Fact]
    public async Task PasswordAndCodes_AreAskedFor()
    {
        Security(data => data.SetPassword("hash"));
        EnableCodes();

        var challenge = await _authenticator.BeginUserLoginAsync(Identity);

        Assert.NotNull(challenge);
        Assert.True(challenge.AcceptsCodes);
        Assert.Null(challenge.Passkey);
        Assert.Equal([SecondStageAuthType.Password, SecondStageAuthType.TwoFactor], challenge.Methods);
    }

    [Fact]
    public async Task Passkeys_AreOfferedWithLoginOptions()
    {
        AddPasskey();

        var challenge = await _authenticator.BeginUserLoginAsync(Identity);

        Assert.NotNull(challenge);
        Assert.False(challenge.AcceptsCodes);
        Assert.NotNull(challenge.Passkey);
        Assert.Equal([SecondStageAuthType.FIDO2], challenge.Methods);
    }

    [Fact]
    public async Task Passkeys_ThatCannotBeChecked_StillKeepTheUserOut()
    {
        AddPasskey();
        _passkeyService.Setup(p => p.IsAvailable).Returns(false);

        var challenge = await _authenticator.BeginUserLoginAsync(Identity);

        Assert.NotNull(challenge);
        Assert.False(challenge.AcceptsCodes);
        Assert.Null(challenge.Passkey);
    }

    [Fact]
    public async Task PasskeyFlag_WithNoEnabledPasskeyLeft_NeedsNoSecondStage()
    {
        AddPasskey(enabled: false);

        Assert.Null(await _authenticator.BeginUserLoginAsync(Identity));
    }

    [Fact]
    public async Task RightPasswordAndCode_Pass()
    {
        Security(data => data.SetPassword("hash"));
        EnableCodes();

        Assert.True(await _authenticator.CheckUserLoginData(UserId, "secret", "123456"));
    }

    [Theory]
    [InlineData("wrong", "123456")]
    [InlineData("secret", "654321")]
    [InlineData(null, "123456")]
    [InlineData("secret", null)]
    public async Task WrongOrMissingPart_Fails(string? password, string? code)
    {
        Security(data => data.SetPassword("hash"));
        EnableCodes();

        Assert.False(await _authenticator.CheckUserLoginData(UserId, password, code));
    }

    [Fact]
    public async Task Code_IsGoodForOneLogin()
    {
        EnableCodes(step: 100);

        Assert.True(await _authenticator.CheckUserLoginData(UserId, null, "123456"));
        Assert.False(await _authenticator.CheckUserLoginData(UserId, null, "123456"));
    }

    [Fact]
    public async Task Code_IsNotBurnedByAWrongPassword()
    {
        Security(data => data.SetPassword("hash"));
        EnableCodes();

        Assert.False(await _authenticator.CheckUserLoginData(UserId, "wrong", "123456"));
        Assert.True(await _authenticator.CheckUserLoginData(UserId, "secret", "123456"));
    }

    [Fact]
    public async Task UserWithPasskeysAlone_CannotPassWithEmptyCodes()
    {
        AddPasskey();

        Assert.False(await _authenticator.CheckUserLoginData(UserId));
    }

    [Fact]
    public async Task Passkey_ThatHolds_PassesAndRecordsItsUse()
    {
        var passkey = AddPasskey();
        _passkeyService.Setup(p => p.VerifyLoginAsync("{}", It.IsAny<PasskeyAssertion>(), passkey.PublicKey, 0,
            UserId, It.IsAny<CancellationToken>())).ReturnsAsync(7u);

        Assert.True(await _authenticator.CheckPasskeyAsync(UserId, "{}", Assertion(passkey.CredentialId)));

        var saved = Assert.Single(_passkeys.All);
        Assert.Equal(7u, saved.SignatureCounter);
        Assert.NotNull(saved.LastUsedAt);
    }

    [Fact]
    public async Task Passkey_OfAnotherUser_Fails()
    {
        var passkey = UserPasskey.Create(UserId + 1, [9], [2], 0, null, Guid.Empty, DateTime.UtcNow);
        await _passkeys.AddAsync(passkey);
        _passkeyService.Setup(p => p.VerifyLoginAsync(It.IsAny<string>(), It.IsAny<PasskeyAssertion>(),
            It.IsAny<byte[]>(), It.IsAny<uint>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1u);

        Assert.False(await _authenticator.CheckPasskeyAsync(UserId, "{}", Assertion([9])));
    }

    [Fact]
    public async Task Passkey_ThatIsDisabled_Fails()
    {
        var passkey = AddPasskey(enabled: false);
        _passkeyService.Setup(p => p.VerifyLoginAsync(It.IsAny<string>(), It.IsAny<PasskeyAssertion>(),
            It.IsAny<byte[]>(), It.IsAny<uint>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1u);

        Assert.False(await _authenticator.CheckPasskeyAsync(UserId, "{}", Assertion(passkey.CredentialId)));
    }

    private static PasskeyAssertion Assertion(byte[] credentialId) => new(credentialId, [1], [2], [3], null);
}
