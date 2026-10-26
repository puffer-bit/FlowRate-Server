using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Core.Models.Push;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Repositories;
using Shared.Enums;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class PushDeviceRepositoryTests : IDisposable
{
    private const string Coordinator = "coordinator";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"push-{Guid.NewGuid():N}.db");
    private readonly PushDeviceRepository _repository;

    public PushDeviceRepositoryTests()
    {
        var factory = new TestContextFactory(new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options);
        _repository = new PushDeviceRepository(factory);

        using var context = factory.CreateDbContext();
        context.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    private static PushDeviceModel Device(int userId, string token, byte keyByte = 1, string coordinator = Coordinator) =>
        new(coordinator, userId, token, PushPlatform.Android, new string('a', 32),
            Enumerable.Repeat(keyByte, 32).ToArray());

    [Fact]
    public async Task Save_StoresTheDeviceOfItsCoordinator()
    {
        await _repository.SaveAsync(Device(1, "token"));
        await _repository.SaveAsync(Device(1, "token", coordinator: "other"));

        var device = Assert.Single(await _repository.GetAllAsync(Coordinator));
        Assert.Equal(1, device.UserId);
        Assert.Equal("token", device.Token);
        Assert.Equal(32, device.Key.Length);
    }

    [Fact]
    public async Task Save_OfAKnownToken_ReplacesItsOwnerAndKey()
    {
        await _repository.SaveAsync(Device(1, "token", keyByte: 1));
        await _repository.SaveAsync(Device(2, "token", keyByte: 9));

        var device = Assert.Single(await _repository.GetAllAsync(Coordinator));
        Assert.Equal(2, device.UserId);
        Assert.Equal(9, device.Key[0]);
    }

    [Fact]
    public async Task Remove_ByUser_LeavesTheDeviceOfAnotherUser()
    {
        await _repository.SaveAsync(Device(1, "token"));

        await _repository.RemoveAsync(Coordinator, 2, "token");
        Assert.Single(await _repository.GetAllAsync(Coordinator));

        await _repository.RemoveAsync(Coordinator, 1, "token");
        Assert.Empty(await _repository.GetAllAsync(Coordinator));
    }

    [Fact]
    public async Task Remove_ByToken_RemovesIt()
    {
        await _repository.SaveAsync(Device(1, "one"));
        await _repository.SaveAsync(Device(1, "two"));

        await _repository.RemoveAsync(Coordinator, "one");

        Assert.Equal("two", Assert.Single(await _repository.GetAllAsync(Coordinator)).Token);
    }

    private sealed class TestContextFactory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task FindByReplyToken_ReturnsTheDeviceOfItsCoordinatorOnly()
    {
        var hash = Enumerable.Repeat((byte)3, 32).ToArray();
        await _repository.SaveAsync(Device(1, "token") with { ReplyTokenHash = hash });
        await _repository.SaveAsync(Device(2, "other-token"));

        var device = await _repository.FindByReplyTokenAsync(Coordinator, hash);

        Assert.Equal(1, device?.UserId);
        Assert.Null(await _repository.FindByReplyTokenAsync("other", hash));
        Assert.Null(await _repository.FindByReplyTokenAsync(Coordinator, new byte[32]));
    }

    [Fact]
    public async Task Save_OfAKnownToken_ReplacesItsReplyToken()
    {
        var first = Enumerable.Repeat((byte)1, 32).ToArray();
        var second = Enumerable.Repeat((byte)2, 32).ToArray();
        await _repository.SaveAsync(Device(1, "token") with { ReplyTokenHash = first });
        await _repository.SaveAsync(Device(1, "token") with { ReplyTokenHash = second });

        Assert.Null(await _repository.FindByReplyTokenAsync(Coordinator, first));
        Assert.NotNull(await _repository.FindByReplyTokenAsync(Coordinator, second));
    }
}
