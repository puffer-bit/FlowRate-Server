using Server.Core.Models.Roles;
using Shared.Enums;

namespace Server.Core.Tests.Models;

public class RoleSetTests
{
    private const string CoordinatorId = "coordinator";
    private const int EveryoneId = 1;
    private const int MemberId = 2;
    private const int ModeratorId = 3;
    private const int AdminId = 4;

    private static readonly RoleSet Roles = new([
        new RoleModel(AdminId, CoordinatorId, "Admin", null, 3, Permission.Administrator, false, 0),
        new RoleModel(EveryoneId, CoordinatorId, "@everyone", null, 0, Permission.CreateSessions, true, 0),
        new RoleModel(ModeratorId, CoordinatorId, "Moderator", 0xFF0000, 2,
            Permission.KickMembers | Permission.ManageRoles, false, 0),
        new RoleModel(MemberId, CoordinatorId, "Member", null, 1, Permission.ManageMessages, false, 0)
    ]);

    [Fact]
    public void Roles_AreOrderedLowestFirst()
    {
        Assert.Equal([EveryoneId, MemberId, ModeratorId, AdminId], Roles.Roles.Select(role => role.Id));
        Assert.Equal(EveryoneId, Roles.Everyone.Id);
    }

    [Fact]
    public void PermissionsOf_NoRoles_AreThoseOfEveryone()
    {
        Assert.Equal(Permission.CreateSessions, Roles.PermissionsOf([]));
    }

    [Fact]
    public void PermissionsOf_CombinesRolesWithEveryone()
    {
        Assert.Equal(Permission.CreateSessions | Permission.ManageMessages | Permission.KickMembers
                     | Permission.ManageRoles, Roles.PermissionsOf([MemberId, ModeratorId]));
    }

    [Fact]
    public void PermissionsOf_Administrator_IsAll()
    {
        Assert.Equal(Permission.All, Roles.PermissionsOf([AdminId]));
        Assert.True(Roles.Has([AdminId], Permission.ManageCoordinatorSettings | Permission.BanMembers));
    }

    [Fact]
    public void PermissionsOf_UnknownRole_IsIgnored()
    {
        Assert.Equal(Permission.CreateSessions, Roles.PermissionsOf([99]));
        Assert.Equal(0, Roles.HighestPositionOf([99]));
    }

    [Fact]
    public void Outranks_OnlyAHigherRole()
    {
        Assert.True(Roles.Outranks([ModeratorId], [MemberId]));
        Assert.True(Roles.Outranks([MemberId], []));
        Assert.False(Roles.Outranks([MemberId], [ModeratorId]));
        Assert.False(Roles.Outranks([], []));
    }

    [Fact]
    public void Outranks_SameHighestRole_IsNot_EvenForAdministrators()
    {
        Assert.False(Roles.Outranks([AdminId], [AdminId, MemberId]));
    }

    [Fact]
    public void CanManage_OnlyRolesBelowTheHighest()
    {
        Assert.True(Roles.CanManage([ModeratorId], Roles.Find(MemberId)!));
        Assert.True(Roles.CanManage([ModeratorId], Roles.Everyone));
        Assert.False(Roles.CanManage([ModeratorId], Roles.Find(ModeratorId)!));
        Assert.False(Roles.CanManage([ModeratorId], Roles.Find(AdminId)!));
        Assert.False(Roles.CanManage([], Roles.Everyone));
    }

    [Fact]
    public void CanGrant_OnlyOwnPermissions()
    {
        Assert.True(Roles.CanGrant([ModeratorId], Permission.KickMembers | Permission.CreateSessions));
        Assert.False(Roles.CanGrant([ModeratorId], Permission.BanMembers));
        Assert.True(Roles.CanGrant([AdminId], Permission.All));
    }

    [Fact]
    public void Constructor_WithoutEveryone_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RoleSet([Roles.Find(AdminId)!]));
    }
}

public class RoleModelTests
{
    [Fact]
    public void Everyone_KeepsItsNameAndHasNoColor()
    {
        var everyone = RoleModel.CreateEveryone("c", Permission.None);

        everyone.Change("Renamed", 0x00FF00, Permission.CreateSessions);

        Assert.Equal(RoleModel.EveryoneName, everyone.Name);
        Assert.Null(everyone.Color);
        Assert.Equal(Permission.CreateSessions, everyone.Permissions);
        Assert.Equal(1, everyone.Revision);
    }

    [Fact]
    public void Everyone_CannotMove()
    {
        Assert.Throws<InvalidOperationException>(() => RoleModel.CreateEveryone("c", Permission.None).MoveTo(1));
    }

    [Fact]
    public void Role_CannotMoveToTheEveryonePosition()
    {
        Assert.Throws<InvalidOperationException>(() => RoleModel.Create("c", "Member", null, 1, Permission.None)
            .MoveTo(0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@everyone")]
    [InlineData("a name that is far too long for a role")]
    public void Create_InvalidName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => RoleModel.Create("c", name, null, 1, Permission.None));
    }

    [Fact]
    public void Create_TrimsName()
    {
        Assert.Equal("Member", RoleModel.Create("c", "  Member ", null, 1, Permission.None).Name);
    }

    [Fact]
    public void Create_UnknownPermission_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RoleModel.Create("c", "Member", null, 1, (Permission)(1UL << 63)));
    }

    [Fact]
    public void Create_ColorOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RoleModel.Create("c", "Member", 0x1000000, 1, Permission.None));
    }
}
