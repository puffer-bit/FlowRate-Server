using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Server.Application.Orchestrator.Security;
using Server.Asp.Web.Api;
using Server.Asp.Web.Api.Contracts;
using Server.Core.Models.User;

namespace Server.Asp.Tests.Web.Api;

public class UserSecurityControllerTests
{
    private const string CoordinatorId = "coordinator";
    private const int UserId = 1;

    private readonly Mock<IUserSecurityService> _security = new();
    private readonly UserSecurityController _controller;

    public UserSecurityControllerTests()
    {
        var data = UserSecurityData.CreateEmpty(UserId);
        data.SetPassword("hash");
        var passkey = UserPasskey.Create(UserId, [1], [2], 0, "Key", Guid.Empty, DateTime.UtcNow);
        passkey.Id = 5;
        _security.Setup(s => s.GetAsync(CoordinatorId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSecurityOverview(data, [passkey], true));

        _controller = new UserSecurityController(_security.Object);
    }

    [Fact]
    public async Task Security_ShowsWhatTheUserHas_WithoutSecrets()
    {
        var result = await _controller.Get(CoordinatorId, UserId, CancellationToken.None);

        var dto = result.Value!;
        Assert.True(dto.HasPassword);
        Assert.False(dto.IsTwoFactorEnabled);
        Assert.Equal(5, Assert.Single(dto.Passkeys).Id);
    }

    [Fact]
    public async Task UnknownUser_IsNotFound()
    {
        var result = await _controller.Get(CoordinatorId, 99, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Change_AnswersWithTheNewState()
    {
        _security.Setup(s => s.SetPasswordAsync(CoordinatorId, UserId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserSecurityStatus.Done);

        var result = await _controller.SetPassword(CoordinatorId, UserId, new SetPasswordDto(), CancellationToken.None);

        Assert.NotNull(result.Value);
    }

    [Theory]
    [InlineData(UserSecurityStatus.Refused, 400)]
    [InlineData(UserSecurityStatus.Invalid, 400)]
    [InlineData(UserSecurityStatus.Unavailable, 409)]
    [InlineData(UserSecurityStatus.UserNotFound, 404)]
    public async Task Failure_IsAProblem(UserSecurityStatus status, int code)
    {
        _security.Setup(s => s.EnableTwoFactorAsync(CoordinatorId, UserId, "1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var result = await _controller.EnableTwoFactor(CoordinatorId, UserId, new TwoFactorCodeDto { Code = "1" },
            CancellationToken.None);

        Assert.Equal(code, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task PasskeyOptions_GoOutAsTheyAre()
    {
        _security.Setup(s => s.BeginPasskeyRegistrationAsync(CoordinatorId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserSecurityStatus.Done, "{\"challenge\":\"x\"}"));

        var result = await _controller.BeginPasskeyRegistration(CoordinatorId, UserId, CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("{\"challenge\":\"x\"}", content.Content);
        Assert.Equal("application/json", content.ContentType);
    }

    [Fact]
    public async Task Passkey_WithoutACredential_IsRefused()
    {
        var result = await _controller.RegisterPasskey(CoordinatorId, UserId, new RegisterPasskeyDto(),
            CancellationToken.None);

        Assert.Equal(400, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        _security.Verify(s => s.RegisterPasskeyAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Passkey_IsHandedOnAsJson()
    {
        _security.Setup(s => s.RegisterPasskeyAsync(CoordinatorId, UserId, "Key", "{\"id\":\"AQ\"}",
            It.IsAny<CancellationToken>())).ReturnsAsync(UserSecurityStatus.Done);

        var result = await _controller.RegisterPasskey(CoordinatorId, UserId, new RegisterPasskeyDto
        {
            Label = "Key",
            Credential = JsonDocument.Parse("{\"id\":\"AQ\"}").RootElement
        }, CancellationToken.None);

        Assert.NotNull(result.Value);
    }
}
