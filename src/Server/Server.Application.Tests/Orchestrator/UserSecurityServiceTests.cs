using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Security;
using Server.Application.Orchestrator.Security;
using Server.Application.Orchestrator.Settings;
using Server.Application.Tests.Fakes;
using Server.Core.Models.Coordinator;
using Server.Core.Models.User;
using Shared.Enums.User;

namespace Server.Application.Tests.Orchestrator;

public class UserSecurityServiceTests
{
    private const int UserId = 1;

    private readonly CoordinatorInstanceModel _coordinator =
        CoordinatorInstanceModel.Create("Main", "127.0.0.1", 5000, DateTime.UtcNow);
    private readonly InMemoryUserSecurityDataRepository _security = new();
    private readonly InMemoryUserPasskeyRepository _passkeys = new();
    private readonly Mock<ITwoFactorService> _codes = new();
    private readonly Mock<IPasskeyService> _passkeyService = new();
    private readonly FakeTimeProvider _time = new();
    private readonly UserSecurityService _service;

    public UserSecurityServiceTests()
    {
        var coordinators = new Mock<ICoordinatorSettingsService>();
        coordinators.Setup(c => c.GetAsync(_coordinator.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_coordinator);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            new UserModel(UserId, _coordinator.Id, "alice", UserState.Normal, UserStatus.Online, DateTime.UtcNow,
                DateTime.UtcNow));
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string?>())).Returns((string? raw) => "hash:" + raw);
        _codes.Setup(c => c.GenerateSecretKey()).Returns("SECRET");
        _codes.Setup(c => c.GetQrCodeUri("alice", "Main", "SECRET")).Returns("otpauth://uri");
        long step = 1;
        _codes.Setup(c => c.ValidateCode("SECRET", "123456", out step)).Returns(true);
        _passkeyService.Setup(p => p.IsAvailable).Returns(true);
        _passkeyService.Setup(p => p.BeginRegistration(UserId, "alice", It.IsAny<IReadOnlyList<byte[]>>()))
            .Returns("{options}");

        _service = new UserSecurityService(coordinators.Object, users.Object, _security, _passkeys, hasher.Object,
            _codes.Object, _passkeyService.Object, _time, NullLogger<UserSecurityService>.Instance);
    }

    private UserSecurityData Saved => _security.Saved(UserId)!;

    [Fact]
    public async Task UserOfAnotherCoordinator_IsNotFound()
    {
        Assert.Null(await _service.GetAsync("other", UserId));
        Assert.Equal(UserSecurityStatus.UserNotFound, await _service.SetPasswordAsync("other", UserId, "password1"));
    }

    [Fact]
    public async Task Password_IsHashed_AndCanBeRemoved()
    {
        Assert.Equal(UserSecurityStatus.Done, await _service.SetPasswordAsync(_coordinator.Id, UserId, "password1"));
        Assert.Equal("hash:password1", Saved.PasswordHash);

        Assert.Equal(UserSecurityStatus.Done, await _service.SetPasswordAsync(_coordinator.Id, UserId, null));
        Assert.False(Saved.IsPasswordEnabled);
    }

    [Theory]
    [InlineData("short")]
    [InlineData(null)]
    public async Task Password_OutOfItsLength_IsRefused(string? tooShort)
    {
        var password = tooShort ?? new string('a', 129);

        Assert.Equal(UserSecurityStatus.Invalid, await _service.SetPasswordAsync(_coordinator.Id, UserId, password));
    }

    [Fact]
    public async Task Codes_AreEnabled_OnlyByACodeOfTheOfferedSecret()
    {
        var (status, offer) = await _service.OfferTwoFactorAsync(_coordinator.Id, UserId);
        Assert.Equal(UserSecurityStatus.Done, status);
        Assert.Equal(new TwoFactorOffer("SECRET", "otpauth://uri"), offer);
        Assert.True(Saved.IsTwoFactorPending);

        Assert.Equal(UserSecurityStatus.Refused, await _service.EnableTwoFactorAsync(_coordinator.Id, UserId, "000000"));
        Assert.False(Saved.IsTwoFactorEnabled);

        Assert.Equal(UserSecurityStatus.Done, await _service.EnableTwoFactorAsync(_coordinator.Id, UserId, "123456"));
        Assert.True(Saved.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task EnabledCodes_AreNotReplacedByANewOffer()
    {
        await _service.OfferTwoFactorAsync(_coordinator.Id, UserId);
        await _service.EnableTwoFactorAsync(_coordinator.Id, UserId, "123456");

        var (status, _) = await _service.OfferTwoFactorAsync(_coordinator.Id, UserId);

        Assert.Equal(UserSecurityStatus.Invalid, status);
        Assert.Equal(UserSecurityStatus.Done, await _service.DisableTwoFactorAsync(_coordinator.Id, UserId));
        Assert.False(Saved.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task Confirming_WithNothingOffered_IsInvalid()
    {
        Assert.Equal(UserSecurityStatus.Invalid, await _service.EnableTwoFactorAsync(_coordinator.Id, UserId, "123456"));
    }

    [Fact]
    public async Task Passkey_IsRegisteredFromTheOptionsGiven_AndTurnsPasskeysOn()
    {
        _passkeyService.Setup(p => p.FinishRegistrationAsync("{options}", "{credential}",
                It.IsAny<Func<byte[], Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyRegistration([1], [2], 0, Guid.Empty));

        var (status, options) = await _service.BeginPasskeyRegistrationAsync(_coordinator.Id, UserId);
        Assert.Equal((UserSecurityStatus.Done, "{options}"), (status, options));

        Assert.Equal(UserSecurityStatus.Done,
            await _service.RegisterPasskeyAsync(_coordinator.Id, UserId, "YubiKey", "{credential}"));

        Assert.Equal("YubiKey", Assert.Single(_passkeys.All).Label);
        Assert.True(Saved.IsFIDO2Enabled);
    }

    [Fact]
    public async Task Passkey_WithoutFreshOptions_IsInvalid()
    {
        await _service.BeginPasskeyRegistrationAsync(_coordinator.Id, UserId);
        _time.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(UserSecurityStatus.Invalid,
            await _service.RegisterPasskeyAsync(_coordinator.Id, UserId, null, "{credential}"));
        Assert.Equal(UserSecurityStatus.Invalid,
            await _service.RegisterPasskeyAsync(_coordinator.Id, UserId, null, "{credential}"));
    }

    [Fact]
    public async Task Passkey_ThatDoesNotHold_IsRefused_AndItsOptionsAreUsedUp()
    {
        await _service.BeginPasskeyRegistrationAsync(_coordinator.Id, UserId);

        Assert.Equal(UserSecurityStatus.Refused,
            await _service.RegisterPasskeyAsync(_coordinator.Id, UserId, null, "{credential}"));
        Assert.Equal(UserSecurityStatus.Invalid,
            await _service.RegisterPasskeyAsync(_coordinator.Id, UserId, null, "{credential}"));
        Assert.Empty(_passkeys.All);
    }

    [Fact]
    public async Task Passkeys_WithoutARelyingParty_AreUnavailable()
    {
        _passkeyService.Setup(p => p.IsAvailable).Returns(false);

        var (status, _) = await _service.BeginPasskeyRegistrationAsync(_coordinator.Id, UserId);

        Assert.Equal(UserSecurityStatus.Unavailable, status);
    }

    [Fact]
    public async Task RemovingTheLastPasskey_TurnsPasskeysOff()
    {
        await _passkeys.AddAsync(UserPasskey.Create(UserId, [1], [2], 0, null, Guid.Empty, DateTime.UtcNow));
        var data = UserSecurityData.CreateEmpty(UserId);
        data.SetPasskeysEnabled(true);
        await _security.UpdateAsync(data);
        var id = _passkeys.All[0].Id;

        Assert.Equal(UserSecurityStatus.Done, await _service.DeletePasskeyAsync(_coordinator.Id, UserId, id));

        Assert.Empty(_passkeys.All);
        Assert.False(Saved.IsFIDO2Enabled);
    }

    [Fact]
    public async Task PasskeyOfAnotherUser_IsNotRemoved()
    {
        await _passkeys.AddAsync(UserPasskey.Create(UserId + 1, [1], [2], 0, null, Guid.Empty, DateTime.UtcNow));

        Assert.Equal(UserSecurityStatus.PasskeyNotFound,
            await _service.DeletePasskeyAsync(_coordinator.Id, UserId, _passkeys.All[0].Id));
        Assert.Single(_passkeys.All);
    }
}
