using Shared.Models.Responses;
using Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Security;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Handlers.Users.Security;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Roles;
using Server.Application.Orchestrator.Security;
using Server.Application.Orchestrator.Settings;
using Server.Application.Tests.Fakes;
using Server.Core.Models.Coordinator;
using Server.Core.Models.User;
using Shared.Enums.User;
using Shared.Models;
using Shared.Models.DataTransferObjects;
using Shared.Models.Requests.Coordinator.Security;
using Shared.Models.Responses.Coordinator.Security;

namespace Server.Application.Tests.Coordinator;

public class UserSecurityHandlersTests
{
    private const int UserId = 1;
    private const string Connection = "connection";

    private readonly CoordinatorInstanceModel _coordinator =
        CoordinatorInstanceModel.Create("Main", "127.0.0.1", 5000, DateTime.UtcNow);
    private readonly InMemoryUserSecurityDataRepository _security = new();
    private readonly InMemoryUserPasskeyRepository _passkeys = new();
    private readonly Mock<IPasskeyService> _passkeyService = new();
    private readonly Mock<IMessageSender> _sender = new();
    private readonly List<BaseMessage> _sent = [];
    private readonly UserSecurityReplies _replies;

    public UserSecurityHandlersTests()
    {
        var coordinators = new Mock<ICoordinatorSettingsService>();
        coordinators.Setup(c => c.GetAsync(_coordinator.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_coordinator);
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            new UserModel(UserId, _coordinator.Id, "alice", UserState.Normal, UserStatus.Online, DateTime.UtcNow,
                DateTime.UtcNow));
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string?>())).Returns((string? raw) => "hash:" + raw);
        hasher.Setup(h => h.VerifyPassword(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns((string? hash, string? raw) => hash == "hash:" + raw);
        var codes = new Mock<ITwoFactorService>();
        codes.Setup(c => c.GenerateSecretKey()).Returns("SECRET");
        codes.Setup(c => c.GetQrCodeUri(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns("otpauth://x");
        long step = 1;
        codes.Setup(c => c.ValidateCode("SECRET", "123456", out step)).Returns(true);
        _passkeyService.Setup(p => p.IsAvailable).Returns(true);
        _passkeyService.Setup(p => p.BeginRegistration(UserId, "alice", It.IsAny<IReadOnlyList<byte[]>>()))
            .Returns("{options}");
        _sender.Setup(s => s.SendMessageById(Connection, It.IsAny<BaseMessage>()))
            .Callback((string _, BaseMessage message) => _sent.Add(message));

        var service = new UserSecurityService(coordinators.Object, users.Object, _security, _passkeys, hasher.Object,
            codes.Object, _passkeyService.Object, new FakeTimeProvider(), NullLogger<UserSecurityService>.Instance);
        _replies = new UserSecurityReplies(service, _coordinator.Id);
    }

    private async Task<T> Send<TRequest, T>(IMessageHandler<TRequest> handler, TRequest request)
        where TRequest : Shared.Models.Requests.Request where T : BaseMessage
    {
        await handler.HandleMessage(request with { SenderId = UserId }, Connection);
        return Assert.IsAssignableFrom<T>(_sent[^1]);
    }

    private Task<UserSecurityResponse> SetPassword(UserSecurityProofDTO? proof, string? password) =>
        Send<UserSetPasswordRequest, UserSecurityResponse>(new UserSetPasswordHandler(_sender.Object, _replies),
            new UserSetPasswordRequest(proof, password));

    private async Task<ErrorCode?> TrySetPassword(UserSecurityProofDTO? proof, string? password) =>
        (await Send<UserSetPasswordRequest, Response>(new UserSetPasswordHandler(_sender.Object, _replies),
            new UserSetPasswordRequest(proof, password))).Error();

    private async Task TurnCodesOn(UserSecurityProofDTO? proof = null)
    {
        await Send<UserTwoFactorSetupRequest, UserTwoFactorSetupResponse>(
            new UserTwoFactorSetupHandler(_sender.Object, _replies), new UserTwoFactorSetupRequest(proof));
        await Send<UserTwoFactorEnableRequest, UserSecurityResponse>(
            new UserTwoFactorEnableHandler(_sender.Object, _replies), new UserTwoFactorEnableRequest("123456"));
    }

    [Fact]
    public async Task Info_TellsWhatTheSenderHas()
    {
        var response = await Send<UserSecurityInfoRequest, UserSecurityResponse>(
            new UserSecurityInfoHandler(_sender.Object, _replies), new UserSecurityInfoRequest());

        Assert.False(response.Security!.HasPassword);
        Assert.True(response.Security.PasskeysAvailable);
    }

    [Fact]
    public async Task FirstPassword_NeedsNoProof()
    {
        var response = await SetPassword(null, "first secret");

        Assert.True(response.Security!.HasPassword);
    }

    [Fact]
    public async Task ChangingThePassword_NeedsTheCurrentOne()
    {
        await SetPassword(null, "first secret");

        Assert.Equal(ErrorCode.ProofRequired, await TrySetPassword(null, "second secret"));
        Assert.Equal(ErrorCode.ProofRequired,
            await TrySetPassword(new UserSecurityProofDTO("wrong", null), "second secret"));
        Assert.Null(await TrySetPassword(new UserSecurityProofDTO("first secret", null), "second secret"));
        Assert.Equal("hash:second secret", _security.Saved(UserId)!.PasswordHash);
    }

    [Fact]
    public async Task Codes_AreTurnedOnWithTheOfferedSecret()
    {
        var offer = await Send<UserTwoFactorSetupRequest, UserTwoFactorSetupResponse>(
            new UserTwoFactorSetupHandler(_sender.Object, _replies), new UserTwoFactorSetupRequest(null));
        Assert.Equal(("SECRET", "otpauth://x"), (offer.Secret, offer.Uri));

        var wrong = await Send<UserTwoFactorEnableRequest, Response>(
            new UserTwoFactorEnableHandler(_sender.Object, _replies), new UserTwoFactorEnableRequest("000000"));
        Assert.Equal(ErrorCode.VerificationFailed, wrong.Error());

        var right = await Send<UserTwoFactorEnableRequest, UserSecurityResponse>(
            new UserTwoFactorEnableHandler(_sender.Object, _replies), new UserTwoFactorEnableRequest("123456"));
        Assert.True(right.Security!.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task WithCodesOn_ChangesNeedACode()
    {
        await TurnCodesOn();
        var disable = new UserTwoFactorDisableHandler(_sender.Object, _replies);

        var refused = await Send<UserTwoFactorDisableRequest, Response>(disable,
            new UserTwoFactorDisableRequest(null));
        Assert.Equal(ErrorCode.ProofRequired, refused.Error());
        Assert.True(_security.Saved(UserId)!.IsTwoFactorEnabled);

        var done = await Send<UserTwoFactorDisableRequest, UserSecurityResponse>(disable,
            new UserTwoFactorDisableRequest(new UserSecurityProofDTO(null, "123456")));
        Assert.False(done.Security!.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task PasskeyOptions_NeedTheProof_AndRegistrationAnswersThem()
    {
        await SetPassword(null, "first secret");
        var options = new UserPasskeyOptionsHandler(_sender.Object, _replies);

        var refused = await Send<UserPasskeyOptionsRequest, Response>(options,
            new UserPasskeyOptionsRequest(null));
        Assert.Equal(ErrorCode.ProofRequired, refused.Error());

        var given = await Send<UserPasskeyOptionsRequest, UserPasskeyOptionsResponse>(options,
            new UserPasskeyOptionsRequest(new UserSecurityProofDTO("first secret", null)));
        Assert.Equal("{options}", given.OptionsJson);

        _passkeyService.Setup(p => p.FinishRegistrationAsync("{options}", "{credential}",
                It.IsAny<Func<byte[], Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyRegistration([1], [2], 0, Guid.Empty));
        var registered = await Send<UserPasskeyRegisterRequest, UserSecurityResponse>(
            new UserPasskeyRegisterHandler(_sender.Object, _replies),
            new UserPasskeyRegisterRequest("Phone", "{credential}"));

        Assert.Equal("Phone", Assert.Single(registered.Security!.Passkeys).Label);
    }

    [Fact]
    public async Task Passkey_IsRemovedWithTheProof()
    {
        await _passkeys.AddAsync(UserPasskey.Create(UserId, [1], [2], 0, "Key", Guid.Empty, DateTime.UtcNow));
        await SetPassword(null, "first secret");
        var delete = new UserPasskeyDeleteHandler(_sender.Object, _replies);
        var id = _passkeys.All[0].Id;

        Assert.Equal(ErrorCode.ProofRequired, (await Send<UserPasskeyDeleteRequest, Response>(
            delete, new UserPasskeyDeleteRequest(null, id))).Error());
        Assert.Equal(ErrorCode.PasskeyNotFound, (await Send<UserPasskeyDeleteRequest, Response>(
            delete, new UserPasskeyDeleteRequest(new UserSecurityProofDTO("first secret", null), id + 1))).Error());
        var done = await Send<UserPasskeyDeleteRequest, UserSecurityResponse>(delete,
            new UserPasskeyDeleteRequest(new UserSecurityProofDTO("first secret", null), id));

        Assert.Empty(done.Security!.Passkeys);
    }

    public static TheoryData<BaseMessage> SecurityRequests() =>
    [
        new UserSecurityInfoRequest(), new UserSetPasswordRequest(null, null), new UserTwoFactorSetupRequest(null),
        new UserTwoFactorEnableRequest("1"), new UserTwoFactorDisableRequest(null), new UserPasskeyOptionsRequest(null),
        new UserPasskeyRegisterRequest(null, "{}"), new UserPasskeyDeleteRequest(null, 1)
    ];

    [Theory]
    [MemberData(nameof(SecurityRequests))]
    public void SecurityRequests_AreOpenToTheSender_EvenWhenFrozen(BaseMessage request)
    {
        Assert.Same(RequestPolicy.Open, RequestPolicies.Of(request));
        Assert.True(RequestPolicies.IsAllowedWhenFrozen(request));
    }

    [Theory]
    [MemberData(nameof(SecurityRequests))]
    public void Changes_CountAsLoginAttempts(BaseMessage request)
    {
        var expected = request is UserSecurityInfoRequest ? RequestRateGroup.Query : RequestRateGroup.Authentication;

        Assert.Equal(expected, RequestRateLimits.GroupOf(request));
    }
}
