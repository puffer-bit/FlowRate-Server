using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Core.Models.Roles;
using Server.Core.Models.User;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Repositories;
using Shared.Enums;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class RoleRepositoryTests : IDisposable
{
    private const string CoordinatorId = "coordinator";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"roles-{Guid.NewGuid():N}.db");
    private readonly RoleRepository _repository;
    private readonly UserRepository _users;

    public RoleRepositoryTests()
    {
        var factory = new TestContextFactory(new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options);
        _repository = new RoleRepository(factory);
        _users = new UserRepository(factory);

        using var context = factory.CreateDbContext();
        context.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    private Task<int> AddUserAsync(string name, string coordinatorId = CoordinatorId) =>
        _users.AddAsync(UserModel.Create(coordinatorId, name, null, DateTime.UtcNow), Guid.NewGuid().ToString());

    [Fact]
    public async Task SavedRoles_ComeBackLowestFirst_WithAllPermissionBits()
    {
        await _repository.AddAsync(RoleModel.Create(CoordinatorId, "Admin", 0xABCDEF, 1, Permission.All));
        await _repository.AddAsync(RoleModel.CreateEveryone(CoordinatorId, Permission.CreateSessions));
        await _repository.AddAsync(RoleModel.CreateEveryone("other", Permission.None));

        var roles = await _repository.GetByCoordinatorAsync(CoordinatorId);

        Assert.Equal([0, 1], roles.Select(role => role.Position));
        Assert.True(roles[0].IsEveryone);
        Assert.Equal(Permission.All, roles[1].Permissions);
        Assert.Equal(0xABCDEF, roles[1].Color);
    }

    [Fact]
    public async Task Update_SavesNameColorPermissionsAndRevision()
    {
        var id = await _repository.AddAsync(RoleModel.Create(CoordinatorId, "Member", null, 1, Permission.None));
        var role = (await _repository.GetByCoordinatorAsync(CoordinatorId)).Single();
        role.Change("Members", 0x00FF00, Permission.KickMembers);

        await _repository.UpdateAsync(role);

        var saved = (await _repository.GetByCoordinatorAsync(CoordinatorId)).Single();
        Assert.Equal((id, "Members", 0x00FF00, Permission.KickMembers, 1L),
            (saved.Id, saved.Name, saved.Color, saved.Permissions, saved.Revision));
    }

    [Fact]
    public async Task Assignments_ComeBackWithTheUser()
    {
        var admin = await _repository.AddAsync(RoleModel.Create(CoordinatorId, "Admin", null, 1, Permission.All));
        var userId = await AddUserAsync("alice");

        await _repository.AssignAsync(userId, admin);
        await _repository.AssignAsync(userId, admin);

        Assert.Equal([admin], await _repository.GetUserRoleIdsAsync(CoordinatorId, userId));
        Assert.Equal([admin], (await _users.GetByIdAsync(userId))!.RoleIds);

        await _repository.UnassignAsync(userId, admin);
        Assert.Empty((await _repository.GetUserRoleIdsAsync(CoordinatorId, userId))!);
    }

    [Fact]
    public async Task UserRoles_OfAnotherCoordinator_AreNull()
    {
        var userId = await AddUserAsync("bob", "other");

        Assert.Null(await _repository.GetUserRoleIdsAsync(CoordinatorId, userId));
    }

    [Fact]
    public async Task Delete_TakesTheRoleFromItsUsers()
    {
        var admin = await _repository.AddAsync(RoleModel.Create(CoordinatorId, "Admin", null, 1, Permission.All));
        var alice = await AddUserAsync("alice");
        await AddUserAsync("bob");
        await _repository.AssignAsync(alice, admin);

        var userIds = await _repository.DeleteAsync(admin);

        Assert.Equal([alice], userIds);
        Assert.Empty(await _repository.GetByCoordinatorAsync(CoordinatorId));
        Assert.Empty((await _repository.GetUserRoleIdsAsync(CoordinatorId, alice))!);
    }

    private sealed class TestContextFactory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }
}
