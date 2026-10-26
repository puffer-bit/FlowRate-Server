using Microsoft.Extensions.Logging.Abstractions;
using Server.Application.Orchestrator.Roles;
using Server.Application.Tests.Fakes;
using Server.Core.Models.Roles;
using Server.Core.Models.User;
using Shared.Enums;

namespace Server.Application.Tests.Orchestrator;

public class RoleServiceTests
{
    private const string CoordinatorId = "coordinator";
    private const int Panel = UserModel.SystemUserId;
    private const int AdminUser = 10;
    private const int ModeratorUser = 11;
    private const int PlainUser = 12;
    private const int OtherModeratorUser = 13;

    private readonly InMemoryRoleRepository _repository = new();
    private readonly RoleService _service;
    private readonly List<RolesChange> _changes = [];

    public RoleServiceTests()
    {
        _service = new RoleService(_repository, NullLogger<RoleService>.Instance);
        _service.RolesChanged += _changes.Add;
    }

    private RoleModel Admin => _service.GetLoaded(CoordinatorId).Roles.Single(role => role.Name == "Admin");
    private RoleModel Role(int id) => _service.GetLoaded(CoordinatorId).Find(id)!;

    /// <summary>Default roles, a moderator role below the admin role and a member role below it.</summary>
    private async Task<(int Moderator, int Member)> SetUpAsync()
    {
        await _service.LoadAsync(CoordinatorId);
        var member = (await _service.CreateAsync(CoordinatorId, Panel,
            new RoleChange("Member", null, Permission.None))).Value!.Id;
        var moderator = (await _service.CreateAsync(CoordinatorId, Panel,
            new RoleChange("Moderator", null, Permission.ManageRoles | Permission.KickMembers))).Value!.Id;
        await _service.ReorderAsync(CoordinatorId, Panel, [member, moderator, Admin.Id]);

        _repository.AddUser(CoordinatorId, AdminUser, Admin.Id);
        _repository.AddUser(CoordinatorId, ModeratorUser, moderator);
        _repository.AddUser(CoordinatorId, OtherModeratorUser, moderator);
        _repository.AddUser(CoordinatorId, PlainUser);
        _changes.Clear();
        return (moderator, member);
    }

    [Fact]
    public async Task Load_NewCoordinator_CreatesEveryoneAndAdmin()
    {
        var roles = await _service.LoadAsync(CoordinatorId);

        Assert.Equal(2, roles.Roles.Count);
        Assert.Equal(RoleService.DefaultEveryonePermissions, roles.Everyone.Permissions);
        Assert.Equal(Permission.Administrator, roles.Roles[1].Permissions);
        Assert.Equal(1, roles.Roles[1].Position);
    }

    [Fact]
    public async Task Load_Twice_CreatesDefaultsOnce()
    {
        await _service.LoadAsync(CoordinatorId);
        var other = new RoleService(_repository, NullLogger<RoleService>.Instance);

        Assert.Equal(2, (await other.LoadAsync(CoordinatorId)).Roles.Count);
    }

    [Fact]
    public async Task Create_GoesRightAboveEveryone_AndIsAnnounced()
    {
        await _service.LoadAsync(CoordinatorId);

        var result = await _service.CreateAsync(CoordinatorId, Panel, new RoleChange("Member", 0x123456, Permission.None));

        Assert.Equal(RoleOperationStatus.Done, result.Status);
        Assert.Equal(1, Role(result.Value!.Id).Position);
        Assert.Equal(2, Admin.Position);
        var change = Assert.Single(_changes);
        Assert.True(change.RolesModified);
        Assert.Equal(3, change.Roles.Roles.Count);
    }

    [Fact]
    public async Task Create_WithoutManageRoles_IsRefused()
    {
        await SetUpAsync();

        var result = await _service.CreateAsync(CoordinatorId, PlainUser, new RoleChange("X", null, Permission.None));

        Assert.Equal(RoleOperationStatus.InsufficientPermissions, result.Status);
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task Create_WithPermissionsTheActorLacks_IsRefused()
    {
        await SetUpAsync();

        var result = await _service.CreateAsync(CoordinatorId, ModeratorUser,
            new RoleChange("X", null, Permission.BanMembers));

        Assert.Equal(RoleOperationStatus.HierarchyViolation, result.Status);
    }

    [Fact]
    public async Task Create_InvalidName_IsRefused()
    {
        await _service.LoadAsync(CoordinatorId);

        var result = await _service.CreateAsync(CoordinatorId, Panel, new RoleChange("@everyone", null, Permission.None));

        Assert.Equal(RoleOperationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Update_StaleRevision_IsConflict_WithTheCurrentRole()
    {
        var (_, member) = await SetUpAsync();
        await _service.UpdateAsync(CoordinatorId, Panel, member, new RoleChange("Members", null, Permission.None), 0);

        var result = await _service.UpdateAsync(CoordinatorId, Panel, member,
            new RoleChange("Other", null, Permission.None), 0);

        Assert.Equal(RoleOperationStatus.Conflict, result.Status);
        Assert.Equal("Members", result.Value!.Name);
        Assert.Equal(1, result.Value.Revision);
    }

    [Fact]
    public async Task Update_RoleAtOrAboveTheActor_IsRefused()
    {
        var (moderator, _) = await SetUpAsync();

        var result = await _service.UpdateAsync(CoordinatorId, ModeratorUser, moderator,
            new RoleChange("Boss", null, Permission.ManageRoles), 0);

        Assert.Equal(RoleOperationStatus.HierarchyViolation, result.Status);
    }

    [Fact]
    public async Task Update_KeepsPermissionsTheActorLacks_WhenNotAdded()
    {
        var (_, member) = await SetUpAsync();
        await _service.UpdateAsync(CoordinatorId, Panel, member, new RoleChange("Member", null, Permission.BanMembers), 0);

        var result = await _service.UpdateAsync(CoordinatorId, ModeratorUser, member,
            new RoleChange("Members", null, Permission.BanMembers | Permission.KickMembers), 1);

        Assert.Equal(RoleOperationStatus.Done, result.Status);
        Assert.Equal("Members", Role(member).Name);
    }

    [Fact]
    public async Task Delete_TakesTheRoleFromItsUsers_AndClosesTheGap()
    {
        var (moderator, member) = await SetUpAsync();

        var result = await _service.DeleteAsync(CoordinatorId, Panel, moderator);

        Assert.Equal(RoleOperationStatus.Done, result.Status);
        Assert.Null(_service.GetLoaded(CoordinatorId).Find(moderator));
        Assert.Equal(1, Role(member).Position);
        Assert.Equal(2, Admin.Position);
        var change = Assert.Single(_changes);
        Assert.Equal([ModeratorUser, OtherModeratorUser], change.UserRoles.Keys.Order());
        Assert.Empty(change.UserRoles[ModeratorUser]);
    }

    [Fact]
    public async Task Delete_Everyone_IsRefused()
    {
        await SetUpAsync();

        var result = await _service.DeleteAsync(CoordinatorId, Panel,
            _service.GetLoaded(CoordinatorId).Everyone.Id);

        Assert.Equal(RoleOperationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Reorder_MissingRole_IsConflict()
    {
        var (moderator, _) = await SetUpAsync();

        var result = await _service.ReorderAsync(CoordinatorId, Panel, [moderator, Admin.Id]);

        Assert.Equal(RoleOperationStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Reorder_RoleAboveTheActor_IsRefused()
    {
        var (moderator, member) = await SetUpAsync();

        var result = await _service.ReorderAsync(CoordinatorId, ModeratorUser, [moderator, member, Admin.Id]);

        Assert.Equal(RoleOperationStatus.HierarchyViolation, result.Status);
        Assert.Equal(2, Role(moderator).Position);
    }

    [Fact]
    public async Task Reorder_ByPanel_MovesRoles()
    {
        var (moderator, member) = await SetUpAsync();

        var result = await _service.ReorderAsync(CoordinatorId, Panel, [moderator, member, Admin.Id]);

        Assert.Equal(RoleOperationStatus.Done, result.Status);
        Assert.Equal(1, Role(moderator).Position);
        Assert.Equal(2, Role(member).Position);
        Assert.Single(_changes);
    }

    [Fact]
    public async Task Assign_ByModerator_ToLowerUser_IsAnnounced()
    {
        var (_, member) = await SetUpAsync();

        var result = await _service.AssignAsync(CoordinatorId, ModeratorUser, PlainUser, member);

        Assert.Equal(RoleOperationStatus.Done, result.Status);
        Assert.Equal([member], result.Value!);
        Assert.Contains(member, _repository.RolesOf(PlainUser));
        var change = Assert.Single(_changes);
        Assert.False(change.RolesModified);
        Assert.Equal([member], change.UserRoles[PlainUser]);
    }

    [Fact]
    public async Task Assign_ToUserOfTheSameRank_IsRefused()
    {
        var (_, member) = await SetUpAsync();

        var result = await _service.AssignAsync(CoordinatorId, ModeratorUser, OtherModeratorUser, member);

        Assert.Equal(RoleOperationStatus.HierarchyViolation, result.Status);
    }

    [Fact]
    public async Task Assign_OwnRoleToOthers_IsRefused()
    {
        var (moderator, _) = await SetUpAsync();

        var result = await _service.AssignAsync(CoordinatorId, ModeratorUser, PlainUser, moderator);

        Assert.Equal(RoleOperationStatus.HierarchyViolation, result.Status);
    }

    [Fact]
    public async Task Assign_Everyone_IsRefused()
    {
        await SetUpAsync();

        var result = await _service.AssignAsync(CoordinatorId, Panel, PlainUser,
            _service.GetLoaded(CoordinatorId).Everyone.Id);

        Assert.Equal(RoleOperationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Assign_UserOfAnotherCoordinator_IsUserNotFound()
    {
        await SetUpAsync();
        _repository.AddUser("other", 50);

        var result = await _service.AssignAsync(CoordinatorId, Panel, 50, Admin.Id);

        Assert.Equal(RoleOperationStatus.UserNotFound, result.Status);
    }

    [Fact]
    public async Task Assign_ByPanel_MakesTheFirstAdmin()
    {
        await SetUpAsync();

        var result = await _service.AssignAsync(CoordinatorId, Panel, PlainUser, Admin.Id);

        Assert.Equal(RoleOperationStatus.Done, result.Status);
        Assert.Equal(Permission.All, _service.GetLoaded(CoordinatorId).PermissionsOf(result.Value!));
    }

    [Fact]
    public async Task Assign_RoleTheUserHas_ChangesNothing()
    {
        var (moderator, _) = await SetUpAsync();

        var result = await _service.AssignAsync(CoordinatorId, Panel, ModeratorUser, moderator);

        Assert.Equal(RoleOperationStatus.Done, result.Status);
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task Unassign_TakesTheRole()
    {
        var (moderator, _) = await SetUpAsync();

        var result = await _service.UnassignAsync(CoordinatorId, AdminUser, ModeratorUser, moderator);

        Assert.Equal(RoleOperationStatus.Done, result.Status);
        Assert.Empty(_repository.RolesOf(ModeratorUser));
        Assert.Single(_changes);
    }
}
