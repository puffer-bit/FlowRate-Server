using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Orchestrator.Roles;
using Server.Application.Orchestrator.Settings;
using Server.Asp.Web.Api;
using Server.Asp.Web.Api.Contracts;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Roles;
using Server.Core.Models.User;
using Shared.Enums;

namespace Server.Asp.Tests.Web.Api;

public class RolesControllerTests
{
    private readonly Mock<IRoleService> _roles = new();
    private readonly Mock<ICoordinatorSettingsService> _settings = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly CoordinatorInstanceModel _model =
        CoordinatorInstanceModel.Create("coordinator", "127.0.0.1", 5000, DateTime.UtcNow);
    private readonly RolesController _controller;

    public RolesControllerTests()
    {
        _settings.Setup(s => s.GetAsync(_model.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_model);
        _controller = new RolesController(_roles.Object, _settings.Object, _users.Object);
    }

    [Fact]
    public async Task GetRoles_UnknownCoordinator_IsNotFound()
    {
        var result = await _controller.GetRoles("missing", default);

        Assert.IsType<NotFoundResult>(result.Result);
        _roles.Verify(r => r.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRoles_ListsSinglePermissions()
    {
        _roles.Setup(r => r.LoadAsync(_model.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new RoleSet([
            new RoleModel(1, _model.Id, "@everyone", null, 0, Permission.CreateSessions | Permission.KickMembers, true, 0),
            new RoleModel(2, _model.Id, "Admin", null, 1, Permission.Administrator, false, 0)
        ]));

        var roles = Assert.IsAssignableFrom<IReadOnlyList<RoleDto>>(
            Assert.IsType<OkObjectResult>((await _controller.GetRoles(_model.Id, default)).Result).Value);

        Assert.Equal([Permission.CreateSessions, Permission.KickMembers], roles[0].Permissions);
        Assert.Equal([Permission.Administrator], roles[1].Permissions);
    }

    [Fact]
    public async Task CreateRole_ActsAsTheSystemUser_WithCombinedPermissions()
    {
        _roles.Setup(r => r.CreateAsync(_model.Id, UserModel.SystemUserId, It.IsAny<RoleChange>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleOperationResult<RoleModel>(RoleOperationStatus.Done,
                RoleModel.Create(_model.Id, "Mods", null, 1, Permission.KickMembers | Permission.BanMembers)));

        var result = await _controller.CreateRole(_model.Id,
            new RoleChangeDto { Name = "Mods", Permissions = [Permission.KickMembers, Permission.BanMembers] }, default);

        Assert.Equal("Mods", result.Value!.Name);
        _roles.Verify(r => r.CreateAsync(_model.Id, UserModel.SystemUserId,
            new RoleChange("Mods", null, Permission.KickMembers | Permission.BanMembers),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRole_OfAChangedRevision_IsAConflict()
    {
        _roles.Setup(r => r.UpdateAsync(_model.Id, UserModel.SystemUserId, 3, It.IsAny<RoleChange>(), 1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleOperationResult<RoleModel>.Failed(RoleOperationStatus.Conflict));

        var result = await _controller.UpdateRole(_model.Id, 3,
            new UpdateRoleDto { Name = "Mods", ExpectedRevision = 1 }, default);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task AssignRole_UnknownUser_IsNotFound()
    {
        _roles.Setup(r => r.AssignAsync(_model.Id, UserModel.SystemUserId, 7, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleOperationResult<IReadOnlyList<int>>.Failed(RoleOperationStatus.UserNotFound));

        var result = await _controller.AssignRole(_model.Id, 7, 2, default);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }
}
