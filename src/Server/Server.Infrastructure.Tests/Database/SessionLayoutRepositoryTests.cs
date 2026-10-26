using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Core.Models.Layout;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Repositories;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class SessionLayoutRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"layout-{Guid.NewGuid():N}.db");
    private readonly SessionLayoutRepository _repository;

    public SessionLayoutRepositoryTests()
    {
        var factory = new TestContextFactory(new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options);
        _repository = new SessionLayoutRepository(factory);

        using var context = factory.CreateDbContext();
        context.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    [Fact]
    public async Task SavedLayout_ComesBackInTheSameOrder()
    {
        var voice = await _repository.AddCategoryAsync("coordinator", "Voice");
        var games = await _repository.AddCategoryAsync("coordinator", "Games");

        await _repository.SaveAsync("coordinator", new SessionLayoutModel([5, 1],
            [(new SessionCategoryModel(games, "Games"), [3]), (new SessionCategoryModel(voice, "Talk"), [4, 2])]));

        var layout = await _repository.GetAsync("coordinator");
        Assert.Equal([5, 1], layout.RootSessionIds);
        Assert.Equal([(games, "Games"), (voice, "Talk")], layout.Categories.Select(c => (c.Category.Id, c.Category.Name)));
        Assert.Equal([4, 2], layout.Categories.Last().SessionIds);
    }

    [Fact]
    public async Task Save_RemovesCategoriesThatAreGone()
    {
        var voice = await _repository.AddCategoryAsync("coordinator", "Voice");
        await _repository.SaveAsync("coordinator", new SessionLayoutModel([], [(new SessionCategoryModel(voice, "Voice"), [1])]));

        await _repository.SaveAsync("coordinator", new SessionLayoutModel([1], []));

        var layout = await _repository.GetAsync("coordinator");
        Assert.Empty(layout.Categories);
        Assert.Equal([1], layout.RootSessionIds);
    }

    [Fact]
    public async Task Layouts_OfCoordinatorsAreSeparate()
    {
        await _repository.SaveAsync("a", new SessionLayoutModel([1], []));
        await _repository.SaveAsync("b", new SessionLayoutModel([2], []));

        Assert.Equal([1], (await _repository.GetAsync("a")).RootSessionIds);
        Assert.Equal([2], (await _repository.GetAsync("b")).RootSessionIds);
    }

    private sealed class TestContextFactory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }
}
