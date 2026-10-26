using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Core.Models.HybridSession;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Repositories;
using Shared.Enums.WebRTC;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class VideoSessionRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"sessions-{Guid.NewGuid():N}.db");
    private readonly TestContextFactory _factory;
    private readonly VideoSessionRepository _repository;

    public VideoSessionRepositoryTests()
    {
        _factory = new TestContextFactory(new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options);
        _repository = new VideoSessionRepository(_factory);

        using var context = _factory.CreateDbContext();
        context.Database.Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    private sealed class TestContextFactory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task Update_SavesTheSettingsAndKeepsTheCreationTime()
    {
        var model = HybridSessionModel.Create("coordinator", "room", 10, 1, WebRTCEngine.Mediasoup);
        model.Id = await _repository.AddAsync(model);
        var createdAt = await CreatedAt(model.Id);

        model.Rename("lounge");
        model.ChangeCapacity(5);
        model.SetHostMustApprove(true);
        await _repository.UpdateAsync(model);

        var saved = await _repository.GetByIdAsync(model.Id);
        Assert.NotNull(saved);
        Assert.Equal(("lounge", 5, true), (saved.Name, saved.Capacity, saved.IsHostMustApprove));
        Assert.Equal(createdAt, await CreatedAt(model.Id));
    }

    [Fact]
    public async Task MediaSettings_AreSavedOnCreationAndOnUpdate()
    {
        var media = new HybridMediaSettings(ScreenCodec: WebRTCVideoCodecs.VP8, IsOpusFecEnabled: false,
            KeyFrameRequestDelayMs: 500, VideoJitterBufferMs: 300);
        var model = HybridSessionModel.Create("coordinator", "room", 10, 1, WebRTCEngine.Mediasoup, media: media);
        model.Id = await _repository.AddAsync(model);

        Assert.Equal(media, (await _repository.GetByIdAsync(model.Id))?.Media);

        var changed = media with { IsAudioNackEnabled = true, OpusPacketLossPercent = 30 };
        model.SetMedia(changed);
        await _repository.UpdateAsync(model);

        Assert.Equal(changed, (await _repository.GetByIdAsync(model.Id))?.Media);
    }

    private async Task<DateTime> CreatedAt(int sessionId)
    {
        await using var context = _factory.CreateDbContext();
        return (await context.VideoSessionsEntities.SingleAsync(s => s.Id == sessionId)).CreatedAt;
    }
}
