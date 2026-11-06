using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Factories;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Tests.Fakes;
using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Enums.WebRTC;

namespace Server.Application.Tests.Coordinator;

public class HybridSessionSettingsTests
{
    private readonly RecordingEventBus _events = new();
    private readonly Mock<IVideoSessionRepository> _repository = new();
    private readonly Dictionary<int, Dictionary<int, PeerSnapshot>> _peers = new();
    private readonly HybridSessionModel _room = new(7, "coordinator", "room", 10, 1, WebRTCEngine.Mediasoup);
    private readonly HybridSessionModel _hall = new(8, "coordinator", "hall", 10, 1, WebRTCEngine.Mediasoup);

    private static readonly HybridMediaSettings LossyMedia = new(CameraCodec: WebRTCVideoCodecs.H264,
        IsAudioNackEnabled: true, OpusPacketLossPercent: 25, VoiceJitterBufferMs: 160);

    private async Task<HybridSessionManager> CreateManager()
    {
        _repository.Setup(r => r.GetByCoordinatorIdAsync("coordinator", It.IsAny<CancellationToken>()))
            .ReturnsAsync([_room, _hall]);

        var factory = new Mock<IVideoSessionFactory>();
        factory.Setup(f => f.CreateHybridSession(It.IsAny<HybridSessionModel>(), It.IsAny<ICoordinatorEventBus>(),
                It.IsAny<ISFUNodeSelector>()))
            .Returns((HybridSessionModel model, ICoordinatorEventBus _, ISFUNodeSelector _) =>
            {
                var peers = _peers[model.Id] = new Dictionary<int, PeerSnapshot>();
                var peerManager = new Mock<IHybridSessionPeerManager>();
                peerManager.Setup(p => p.GetAllPeerSnapshots()).Returns(() => peers);

                var session = new Mock<IHybridSession>();
                session.Setup(s => s.Model).Returns(model);
                session.Setup(s => s.PeerManager).Returns(peerManager.Object);
                return session.Object;
            });

        var manager = new HybridSessionManager(NullLogger.Instance, _events, factory.Object,
            Mock.Of<ISFUNodeSelector>(), _repository.Object, "coordinator");
        await manager.LoadAsync();
        return manager;
    }

    private void AddPeers(int sessionId, int count)
    {
        for (var userId = 1; userId <= count; userId++)
            _peers[sessionId][userId] = new PeerSnapshot(userId, HybridSessionPeerState.Connected, true, true, false, false);
    }

    [Fact]
    public async Task ChangeSettings_SavesAndAnnouncesTheNewSettings()
    {
        using var manager = await CreateManager();

        var result = await manager.ChangeSettingsAsync(7, "lounge", 5, true, LossyMedia, 3);

        Assert.Equal(null, result);
        Assert.Equal(("lounge", 5, true), (_room.Name, _room.Capacity, _room.IsHostMustApprove));
        Assert.Equal(LossyMedia, _room.Media);
        _repository.Verify(r => r.UpdateAsync(_room, It.IsAny<CancellationToken>()), Times.Once);
        var evt = Assert.Single(_events.Of<HybridSessionReconfiguredEvent>());
        Assert.Same(_room, evt.Session);
        Assert.Equal(3, evt.UserId);
    }

    [Fact]
    public async Task ChangeSettings_KeepingItsOwnName_IsAllowed()
    {
        using var manager = await CreateManager();

        var result = await manager.ChangeSettingsAsync(7, "room", 4, false, HybridMediaSettings.Default, 1);

        Assert.Equal(null, result);
        Assert.Equal(4, _room.Capacity);
    }

    [Fact]
    public async Task ChangeSettings_ToTheNameOfAnotherSession_IsRefused()
    {
        using var manager = await CreateManager();

        var result = await manager.ChangeSettingsAsync(7, "hall", 10, false, HybridMediaSettings.Default, 1);

        Assert.Equal(ErrorCode.NameTaken, result);
        Assert.Equal("room", _room.Name);
        Assert.Empty(_events.Events);
    }

    [Fact]
    public async Task ChangeSettings_BelowThePeersInside_IsRefused()
    {
        using var manager = await CreateManager();
        AddPeers(7, 3);

        var result = await manager.ChangeSettingsAsync(7, "room", 2, false, HybridMediaSettings.Default, 1);

        Assert.Equal(ErrorCode.InvalidValue, result);
        Assert.Equal(10, _room.Capacity);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<HybridSessionModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeSettings_OfAMissingSession_IsRefused()
    {
        using var manager = await CreateManager();

        var result = await manager.ChangeSettingsAsync(99, "room", 10, false, HybridMediaSettings.Default, 1);

        Assert.Equal(ErrorCode.SessionNotFound, result);
    }

    [Fact]
    public async Task ChangeSettings_WhenSavingFails_KeepsTheOldSettings()
    {
        using var manager = await CreateManager();
        _repository.Setup(r => r.UpdateAsync(It.IsAny<HybridSessionModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());

        var result = await manager.ChangeSettingsAsync(7, "lounge", 5, true, LossyMedia, 1);

        Assert.Equal(ErrorCode.Internal, result);
        Assert.Equal(("room", 10, false), (_room.Name, _room.Capacity, _room.IsHostMustApprove));
        Assert.Equal(HybridMediaSettings.Default, _room.Media);
        Assert.Empty(_events.Events);
    }
}
