using Shared.Models.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Application.Coordinator.Events;
using Server.Application.Tests.Fakes;
using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;

namespace Server.Application.Tests.Coordinator;

public class HybridSessionPeerManagerTests
{
    private const int HostId = 1;
    private const int GuestId = 2;
    private const int OtherGuestId = 3;

    private readonly FakeSfuGateway _gateway = new();
    private readonly RecordingEventBus _events = new();

    private async Task<HybridSessionPeerManager> CreateManager(bool isHostMustApprove = false)
    {
        var model = HybridSessionModel.Create("coordinator", "room", 10, HostId, WebRTCEngine.Mediasoup, isHostMustApprove);
        model.Id = 7;

        var manager = new HybridSessionPeerManager(NullLogger.Instance, _events, model);
        await manager.ChangeGatewayAsync(_gateway);
        return manager;
    }

    private static async Task JoinAndConnect(HybridSessionPeerManager manager, params int[] userIds)
    {
        foreach (var userId in userIds)
        {
            Assert.Equal(HybridSessionJoinResult.NoError, (await manager.AddPeerAsync(userId, "epoch")).result);
            Assert.Equal(HybridSessionConnectTransportResult.NoError,
                await manager.ConnectPeerAsync(userId, FakeSfuGateway.Dtls()));
        }
    }

    private static HybridSessionPeerState StateOf(HybridSessionPeerManager manager, int userId) =>
        manager.GetAllPeerSnapshots()[userId].State;

    // Joining

    [Fact]
    public async Task AddPeer_WhenSfuIsNotConnected_ReturnsSfuNotActive()
    {
        var manager = await CreateManager();
        _gateway.IsConnected = false;

        var (result, transport) = await manager.AddPeerAsync(HostId, "epoch");

        Assert.Equal(HybridSessionJoinResult.SFUNotActive, result);
        Assert.Null(transport);
        Assert.False(manager.HasPeer(HostId));
    }

    [Fact]
    public async Task AddPeer_LoadsTheSfuSessionOnlyForTheFirstPeer()
    {
        var manager = await CreateManager();

        var (result, transport) = await manager.AddPeerAsync(HostId, "epoch");
        await manager.AddPeerAsync(GuestId, "epoch");

        Assert.Equal(HybridSessionJoinResult.NoError, result);
        Assert.NotNull(transport);
        Assert.Equal(1, _gateway.CallCount("LoadSession"));
        Assert.Equal(2, _events.Of<HybridSessionPeerJoinedEvent>().Count);
    }

    [Fact]
    public async Task AddPeer_WhenTransportFails_LeavesNoPeerAndUnloadsTheSession()
    {
        var manager = await CreateManager();
        _gateway.TransportResult = SFUTransportCreateResult.SFUNodeError;

        var (result, _) = await manager.AddPeerAsync(HostId, "epoch");

        Assert.Equal(HybridSessionJoinResult.SFUError, result);
        Assert.False(manager.HasPeer(HostId));
        Assert.Equal(1, _gateway.CallCount("UnloadSession"));
        Assert.Empty(_events.Of<HybridSessionPeerJoinedEvent>());
    }

    [Fact]
    public async Task AddPeer_AfterTheSfuNodeChanged_LoadsTheSessionOnTheNewNode()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);

        var newNode = new FakeSfuGateway { NodeId = 2 };
        await manager.ChangeGatewayAsync(newNode);
        Assert.Equal(HybridSessionPeerState.WaitingForServer, StateOf(manager, HostId));

        var (result, _) = await manager.AddPeerAsync(HostId, "epoch");

        Assert.Equal(HybridSessionJoinResult.NoError, result);
        Assert.Equal(1, newNode.CallCount("LoadSession"));
        Assert.Single(_events.Of<HybridSessionSFUNodeChangedEvent>());
    }

    [Fact]
    public async Task ChangeGateway_UnloadsTheSessionFromTheOldNode()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);

        await manager.ChangeGatewayAsync(new FakeSfuGateway { NodeId = 2 });

        Assert.Equal(1, _gateway.CallCount("UnloadSession"));
    }

    [Fact]
    public async Task ChangeGateway_ToNoNode_KeepsThePeersWaitingForANode()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        await manager.ChangeGatewayAsync(null);

        Assert.Null(manager.CurrentGateway);
        Assert.False(manager.IsReady);
        Assert.Equal(HybridSessionPeerState.WaitingForServer, StateOf(manager, HostId));
        Assert.Equal(HybridSessionPeerState.WaitingForServer, StateOf(manager, GuestId));
        Assert.Empty(_events.Of<HybridSessionPeerKickedEvent>());
        var changed = Assert.Single(_events.Of<HybridSessionStateChangedEvent>());
        Assert.Equal(HybridSessionNodeState.WaitingForSfuNode, changed.State);
        Assert.Equal(1, _gateway.CallCount("UnloadSession"));
        Assert.Equal(HybridSessionJoinResult.SFUNotActive, (await manager.AddPeerAsync(HostId, "epoch")).result);

        // The detached node is not followed any more
        await _gateway.RaiseConnectionLost();
        Assert.Empty(_events.Of<HybridSessionSFUNodeDisconnectedEvent>());
    }

    [Fact]
    public async Task ChangeGateway_ToNoNode_WhenTheNodeIsDown_DoesNotCallIt()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);
        await _gateway.RaiseConnectionLost();
        var callsBefore = _gateway.Calls.Count;

        await manager.ChangeGatewayAsync(null);

        Assert.True(manager.HasPeer(HostId));
        Assert.Equal(callsBefore, _gateway.Calls.Count);
    }

    [Fact]
    public async Task ChangeGateway_FromNoNode_ActivatesTheSessionAndLetsThePeersRejoin()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);
        await manager.ChangeGatewayAsync(null);

        var newNode = new FakeSfuGateway { NodeId = 2 };
        await manager.ChangeGatewayAsync(newNode);

        Assert.Equal([HybridSessionNodeState.WaitingForSfuNode, HybridSessionNodeState.Active],
            _events.Of<HybridSessionStateChangedEvent>().Select(changed => changed.State));
        Assert.Single(_events.Of<HybridSessionSFUNodeChangedEvent>());
        Assert.Equal(HybridSessionPeerState.WaitingForServer, StateOf(manager, HostId));
        Assert.Equal(HybridSessionJoinResult.NoError, (await manager.AddPeerAsync(HostId, "epoch")).result);
        Assert.Equal(1, newNode.CallCount("LoadSession"));
    }

    [Fact]
    public async Task ChangeGateway_ToNoNode_Twice_ReportsTheStateOnce()
    {
        var manager = await CreateManager();

        await manager.ChangeGatewayAsync(null);
        await manager.ChangeGatewayAsync(null);

        Assert.Single(_events.Of<HybridSessionStateChangedEvent>());
    }

    [Fact]
    public async Task RemovePeer_WhenTheNodeIsDown_StillLetsThePeerLeave()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);
        await _gateway.RaiseConnectionLost();
        var callsBefore = _gateway.Calls.Count;

        Assert.Equal(LeaveSessionResult.NoError, await manager.RemovePeerAsync(HostId));

        Assert.False(manager.HasPeer(HostId));
        Assert.Equal(callsBefore, _gateway.Calls.Count);
    }

    [Fact]
    public async Task AddPeer_AfterTheSfuConnectionWasLost_LoadsTheSessionAgain()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);

        await _gateway.RaiseConnectionLost();
        Assert.Equal(HybridSessionPeerState.WaitingForServer, StateOf(manager, HostId));
        Assert.Single(_events.Of<HybridSessionSFUNodeDisconnectedEvent>());

        await _gateway.RaiseConnected();
        await manager.AddPeerAsync(HostId, "epoch");

        Assert.Equal(2, _gateway.CallCount("LoadSession"));
    }

    [Fact]
    public async Task AddPeer_RejectedPeer_WaitsForApprovalAgain()
    {
        var manager = await CreateManager(isHostMustApprove: true);
        await manager.AddPeerAsync(HostId, "epoch");
        await manager.AddPeerAsync(GuestId, "epoch");
        await manager.RejectPeerAsync(HostId, GuestId);

        var (result, _) = await manager.AddPeerAsync(GuestId, "epoch");

        Assert.Equal(HybridSessionJoinResult.NoError, result);
        Assert.Equal(HybridSessionPeerState.WaitingForApprove, StateOf(manager, GuestId));
    }

    // Devices

    [Fact]
    public async Task AddPeer_FromAnotherConnection_TakesThePeerOverAndReportsTheOldOne()
    {
        var manager = await CreateManager();
        await manager.AddPeerAsync(GuestId, "epoch", "phone");
        await manager.ConnectPeerAsync(GuestId, FakeSfuGateway.Dtls());

        var (result, transport) = await manager.AddPeerAsync(GuestId, "epoch", "desktop");

        Assert.Equal(HybridSessionJoinResult.NoError, result);
        Assert.NotNull(transport);
        var replaced = Assert.Single(_events.Of<HybridSessionPeerReplacedEvent>());
        Assert.Equal(new HybridSessionPeerReplacedEvent(7, GuestId, "phone"), replaced);
        Assert.Equal(HybridSessionPeerState.Connecting, StateOf(manager, GuestId));
        Assert.True(manager.IsPeerOf(GuestId, "desktop"));
        Assert.False(manager.IsPeerOf(GuestId, "phone"));
    }

    [Fact]
    public async Task AddPeer_AgainFromTheSameConnection_ReplacesNothing()
    {
        var manager = await CreateManager();
        await manager.AddPeerAsync(GuestId, "epoch", "phone");

        await manager.AddPeerAsync(GuestId, "epoch", "phone");

        Assert.Empty(_events.Of<HybridSessionPeerReplacedEvent>());
    }

    [Fact]
    public async Task IsPeerOf_UserNotInTheSession_IsTrue()
    {
        var manager = await CreateManager();

        Assert.True(manager.IsPeerOf(GuestId, "phone"));
    }

    [Fact]
    public async Task RemovePeerOfConnection_TheConnectionTheUserLeft_KeepsThePeer()
    {
        var manager = await CreateManager();
        await manager.AddPeerAsync(GuestId, "epoch", "phone");
        await manager.AddPeerAsync(GuestId, "epoch", "desktop");

        Assert.Equal(LeaveSessionResult.PeerNotExists, await manager.RemovePeerOfConnectionAsync(GuestId, "phone"));
        Assert.True(manager.HasPeer(GuestId));

        Assert.Equal(LeaveSessionResult.NoError, await manager.RemovePeerOfConnectionAsync(GuestId, "desktop"));
        Assert.False(manager.HasPeer(GuestId));
        Assert.Single(_events.Of<HybridSessionPeerLeftEvent>());
    }

    [Fact]
    public async Task StartVoiceShare_GoesToTheConnectionTheListenerTakesPartFrom()
    {
        var manager = await CreateManager();
        await manager.AddPeerAsync(HostId, "epoch", "host");
        await manager.ConnectPeerAsync(HostId, FakeSfuGateway.Dtls());
        await manager.AddPeerAsync(GuestId, "epoch", "guest");
        await manager.ConnectPeerAsync(GuestId, FakeSfuGateway.Dtls());

        await manager.StartVoiceShareAsync(HostId);

        var started = Assert.Single(_events.Of<HybridSessionPeerStartedVoiceShareEvent>());
        Assert.Equal(GuestId, started.ListenerUserId);
        Assert.Equal("guest", started.ListenerConnectionId);
    }

    [Fact]
    public async Task Poke_GoesToTheConnectionThePokedUserTakesPartFrom()
    {
        var manager = await CreateManager();
        await manager.AddPeerAsync(HostId, "epoch", "host");
        await manager.AddPeerAsync(GuestId, "epoch", "guest");

        await manager.PokeAsync(HostId, GuestId, "hi");

        Assert.Equal("guest", Assert.Single(_events.Of<HybridSessionPeerPokedEvent>()).ConnectionId);
    }

    // Admission

    [Fact]
    public async Task ApprovePeer_PendingGuest_BecomesConnectedAndIsAnnouncedOnce()
    {
        var manager = await CreateManager(isHostMustApprove: true);
        await JoinAndConnect(manager, HostId);
        await manager.AddPeerAsync(GuestId, "epoch");
        await manager.ConnectPeerAsync(GuestId, FakeSfuGateway.Dtls());
        Assert.Equal(HybridSessionPeerState.WaitingForApprove, StateOf(manager, GuestId));

        Assert.Equal(ApproveUserSessionResult.NoError, await manager.ApprovePeerAsync(HostId, GuestId));
        Assert.Equal(ApproveUserSessionResult.NoError, await manager.ApprovePeerAsync(HostId, GuestId));

        Assert.Equal(HybridSessionPeerState.Connected, StateOf(manager, GuestId));
        var approved = Assert.Single(_events.Of<HybridSessionPeerApprovedEvent>());
        Assert.Equal(HostId, approved.InitiatorUserId);
    }

    [Fact]
    public async Task ApprovePeer_GuestThatNeedsNoApproval_IsNotAnnounced()
    {
        var manager = await CreateManager(isHostMustApprove: false);
        await JoinAndConnect(manager, HostId, GuestId);

        Assert.Equal(ApproveUserSessionResult.NoError, await manager.ApprovePeerAsync(HostId, GuestId));

        Assert.Empty(_events.Of<HybridSessionPeerApprovedEvent>());
    }

    [Fact]
    public async Task RejectPeer_RemovesThePeerAndClosesItsTransport()
    {
        var manager = await CreateManager(isHostMustApprove: true);
        await manager.AddPeerAsync(HostId, "epoch");
        await manager.AddPeerAsync(GuestId, "epoch");

        Assert.Equal(RejectUserSessionResult.NoError, await manager.RejectPeerAsync(HostId, GuestId, "no"));

        Assert.False(manager.HasPeer(GuestId));
        Assert.Equal(1, _gateway.CallCount($"ClosePeerTransport:{GuestId}"));
        Assert.Single(_events.Of<HybridSessionPeerRejectedEvent>());
    }

    [Fact]
    public async Task PendingGuest_CannotShareMedia_UntilApproved()
    {
        var manager = await CreateManager(isHostMustApprove: true);
        await JoinAndConnect(manager, HostId);
        await manager.AddPeerAsync(GuestId, "epoch");
        await manager.ConnectPeerAsync(GuestId, FakeSfuGateway.Dtls());

        Assert.Equal(StartVoiceShareResult.Rejected, (await manager.StartVoiceShareAsync(GuestId)).result);
        Assert.Equal(StartVideoShareResult.Rejected, (await manager.StartVideoShareAsync(GuestId)).result);
        Assert.Equal(StartScreenShareResult.Rejected, (await manager.StartScreenShareAsync(GuestId, true)).result);
        Assert.Equal(0, _gateway.CallCount($"AddOutbound:{GuestId}"));

        await manager.ApprovePeerAsync(HostId, GuestId);

        Assert.Equal(StartVoiceShareResult.NoError, (await manager.StartVoiceShareAsync(GuestId)).result);
    }

    // Leaving

    [Fact]
    public async Task KickPeer_RemovesTheTargetAndNotTheInitiator()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        var result = await manager.KickPeerAsync(HostId, GuestId, "bye");

        Assert.Equal(LeaveSessionResult.NoError, result);
        Assert.True(manager.HasPeer(HostId));
        Assert.False(manager.HasPeer(GuestId));
        var kicked = Assert.Single(_events.Of<HybridSessionPeerKickedEvent>());
        Assert.Equal((GuestId, HostId, "bye"), (kicked.UserId, kicked.InitiatorUserId, kicked.Reason));
    }

    [Fact]
    public async Task KickPeer_Self_IsRefusedAndKeepsThePeer()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        var result = await manager.KickPeerAsync(HostId, HostId);

        Assert.Equal(LeaveSessionResult.CannotKickSelf, result);
        Assert.True(manager.HasPeer(HostId));
        Assert.Empty(_events.Of<HybridSessionPeerKickedEvent>());
    }

    [Fact]
    public async Task RemovePeer_LastPeerLeaving_UnloadsTheSession()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        await manager.RemovePeerAsync(GuestId);
        Assert.Equal(0, _gateway.CallCount("UnloadSession"));

        await manager.RemovePeerAsync(HostId);

        Assert.Equal(1, _gateway.CallCount("UnloadSession"));
        Assert.Equal(2, _events.Of<HybridSessionPeerLeftEvent>().Count);
    }

    [Fact]
    public async Task RemovePeer_UnknownPeer_ReturnsPeerNotExists()
    {
        var manager = await CreateManager();

        Assert.Equal(LeaveSessionResult.PeerNotExists, await manager.RemovePeerAsync(GuestId));
    }

    [Fact]
    public async Task KickAllPeers_KicksEveryoneWithoutInitiatorAndUnloads()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        await manager.KickAllPeersAsync("closing");

        Assert.Empty(manager.GetAllPeerSnapshots());
        var kicked = _events.Of<HybridSessionPeerKickedEvent>();
        Assert.Equal(2, kicked.Count);
        Assert.All(kicked, e => Assert.Null(e.InitiatorUserId));
        Assert.Equal(1, _gateway.CallCount("UnloadSession"));
    }

    // Voice

    [Fact]
    public async Task StartVoiceShare_IsConsumedOnlyByConnectedListeners()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        await manager.AddPeerAsync(OtherGuestId, "epoch"); // joined, transport not connected yet

        var (result, outbound) = await manager.StartVoiceShareAsync(HostId);

        Assert.Equal(StartVoiceShareResult.NoError, result);
        Assert.NotNull(outbound);
        var started = Assert.Single(_events.Of<HybridSessionPeerStartedVoiceShareEvent>());
        Assert.Equal((HostId, GuestId), (started.UserId, started.ListenerUserId));
        Assert.Equal(0, _gateway.CallCount($"AddInbound:{OtherGuestId}"));
    }

    [Fact]
    public async Task StartVoiceShare_Twice_ReturnsTheSameOutbound()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        var (_, first) = await manager.StartVoiceShareAsync(HostId);
        var (_, second) = await manager.StartVoiceShareAsync(HostId);

        Assert.Equal(first, second);
        Assert.Equal(1, _gateway.CallCount($"AddOutbound:{HostId}"));
        Assert.Equal(1, _gateway.CallCount($"AddInbound:{GuestId}"));
    }

    [Fact]
    public async Task ConnectPeer_ConsumesVoiceThatIsAlreadyShared()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);
        await manager.StartVoiceShareAsync(HostId);

        await JoinAndConnect(manager, GuestId);

        var started = Assert.Single(_events.Of<HybridSessionPeerStartedVoiceShareEvent>());
        Assert.Equal(GuestId, started.ListenerUserId);
    }

    [Fact]
    public async Task StartVoiceShare_CreatesTheInboundsOfAllListenersInParallel()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId, OtherGuestId, 4);
        _gateway.InboundGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var start = manager.StartVoiceShareAsync(HostId);
        await WaitUntil(() => _gateway.InboundsInFlight == 3);
        _gateway.InboundGate.SetResult();
        var (result, _) = await start;

        Assert.Equal(StartVoiceShareResult.NoError, result);
        Assert.Equal(3, _gateway.MaxInboundsInFlight);
        Assert.Equal(3, _events.Of<HybridSessionPeerStartedVoiceShareEvent>().Count);
    }

    [Fact]
    public async Task StartVoiceShare_OneFailedListener_DoesNotFailTheOthers()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId, OtherGuestId);
        _gateway.FailInboundFor.Add(GuestId);

        var (result, _) = await manager.StartVoiceShareAsync(HostId);

        Assert.Equal(StartVoiceShareResult.NoError, result);
        var started = Assert.Single(_events.Of<HybridSessionPeerStartedVoiceShareEvent>());
        Assert.Equal(OtherGuestId, started.ListenerUserId);
    }

    [Fact]
    public async Task StopVoiceShare_RemovesTheOutboundAndAnnouncesIt()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        var (_, outbound) = await manager.StartVoiceShareAsync(HostId);

        var result = await manager.StopVoiceShareAsync(HostId, HostId);

        Assert.Equal(StopVoiceShareResult.NoError, result);
        Assert.Equal(1, _gateway.CallCount($"RemoveOutbound:{outbound!.Id}"));
        var stopped = Assert.Single(_events.Of<HybridSessionPeerStoppedVoiceShareEvent>());
        Assert.Null(stopped.InitiatorUserId);
    }

    [Fact]
    public async Task StartScreenShare_IsAnnouncedButConsumedByNobody()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        var (result, outbound, _) = await manager.StartScreenShareAsync(HostId, withAudio: false);

        Assert.Equal(StartScreenShareResult.NoError, result);
        Assert.Equal((MediaSourceType.Screen, MediaType.Video), (outbound!.SourceType, outbound.MediaType));
        var started = Assert.Single(_events.Of<HybridSessionPeerStartedScreenShareEvent>());
        Assert.Equal((HostId, null, null), (started.UserId, started.ListenerUserId, started.Inbound));
        Assert.True(manager.GetAllPeerSnapshots()[HostId].IsScreenShareActive);
        Assert.False(manager.GetAllPeerSnapshots()[GuestId].IsScreenShareActive);
        Assert.Empty(_events.Of<HybridSessionPeerStartedVoiceShareEvent>());
        Assert.Equal(0, _gateway.CallCount($"AddInbound:{GuestId}"));
    }

    [Fact]
    public async Task WatchScreenShare_CreatesOneInboundForTheWatcherOnly()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId, OtherGuestId);
        await manager.StartScreenShareAsync(HostId, withAudio: false);

        var (result, inbound, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);
        var (againResult, again, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        Assert.Equal(SetScreenShareWatchResult.NoError, result);
        Assert.NotNull(inbound);
        Assert.Equal(SetScreenShareWatchResult.NoError, againResult);
        Assert.Equal(inbound, again);
        Assert.Equal(1, _gateway.CallCount($"AddInbound:{GuestId}"));
        Assert.Equal(0, _gateway.CallCount($"AddInbound:{OtherGuestId}"));
        Assert.Equal(0, _gateway.CallCount($"ResumeInbound:{inbound!.Id}"));
    }

    [Fact]
    public async Task WatchScreenShare_ThatIsNotShared_IsRefused()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        var (neverShared, _, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);
        await manager.StartScreenShareAsync(HostId, withAudio: false);
        await manager.StopScreenShareAsync(HostId, HostId);
        var (stopped, _, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        Assert.Equal(SetScreenShareWatchResult.ScreenShareNotActive, neverShared);
        Assert.Equal(SetScreenShareWatchResult.ScreenShareNotActive, stopped);
        Assert.Equal(0, _gateway.CallCount($"AddInbound:{GuestId}"));
    }

    [Fact]
    public async Task UnwatchScreenShare_PausesTheInbound_AndWatchingAgainResumesIt()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        await manager.StartScreenShareAsync(HostId, withAudio: false);
        var (_, inbound, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);
        await manager.ResumeInboundsAsync(GuestId, [inbound!.Id]);

        var (unwatchResult, _, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, false);
        await manager.SetScreenShareWatchAsync(GuestId, HostId, false);
        var (rewatchResult, resumed, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        Assert.Equal(SetScreenShareWatchResult.NoError, unwatchResult);
        Assert.Equal(SetScreenShareWatchResult.NoError, rewatchResult);
        Assert.Equal(inbound, resumed);
        Assert.Equal(1, _gateway.CallCount($"PauseInbound:{inbound.Id}"));
        Assert.Equal(2, _gateway.CallCount($"ResumeInbound:{inbound.Id}"));
        Assert.Equal(1, _gateway.CallCount($"AddInbound:{GuestId}"));
        Assert.Equal(0, _gateway.CallCount($"RemoveInbound:{inbound.Id}"));
    }

    [Fact]
    public async Task ResumeInbounds_StartsAVoiceInbound_Once()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        await manager.StartVoiceShareAsync(HostId);
        var inbound = Assert.Single(_events.Of<HybridSessionPeerStartedVoiceShareEvent>()).Inbound;

        Assert.Equal(0, _gateway.CallCount($"ResumeInbound:{inbound.Id}"));

        var result = await manager.ResumeInboundsAsync(GuestId, [inbound.Id]);
        var repeated = await manager.ResumeInboundsAsync(GuestId, [inbound.Id]);

        Assert.Equal(ResumeInboundsResult.NoError, result);
        Assert.Equal(ResumeInboundsResult.NoError, repeated);
        Assert.Equal(1, _gateway.CallCount($"ResumeInbound:{inbound.Id}"));
    }

    [Fact]
    public async Task ResumeInbounds_IgnoresInboundsOfOtherListeners_AndUnknownOnes()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId, OtherGuestId);
        await manager.StartVoiceShareAsync(HostId);
        var inbound = _events.Of<HybridSessionPeerStartedVoiceShareEvent>()
            .Single(started => started.ListenerUserId == GuestId).Inbound;

        var result = await manager.ResumeInboundsAsync(OtherGuestId, [inbound.Id, "unknown"]);
        var notPeer = await manager.ResumeInboundsAsync(99, [inbound.Id]);

        Assert.Equal(ResumeInboundsResult.NoError, result);
        Assert.Equal(ResumeInboundsResult.PeerNotExists, notPeer);
        Assert.Equal(0, _gateway.CallCount($"ResumeInbound:{inbound.Id}"));
    }

    [Fact]
    public async Task ResumeInbounds_OfAScreenThatIsNoLongerWatched_KeepsItPaused_UntilWatchedAgain()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        await manager.StartScreenShareAsync(HostId, withAudio: false);
        var (_, inbound, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);
        await manager.SetScreenShareWatchAsync(GuestId, HostId, false);

        await manager.ResumeInboundsAsync(GuestId, [inbound!.Id]);

        Assert.Equal(0, _gateway.CallCount($"ResumeInbound:{inbound.Id}"));

        await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        Assert.Equal(1, _gateway.CallCount($"ResumeInbound:{inbound.Id}"));
    }

    [Fact]
    public async Task WatchScreenShare_Again_BeforeTheInboundIsReady_ResumesItOnlyOnceReady()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        await manager.StartScreenShareAsync(HostId, withAudio: false);
        var (_, inbound, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);
        await manager.SetScreenShareWatchAsync(GuestId, HostId, false);
        await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        Assert.Equal(0, _gateway.CallCount($"ResumeInbound:{inbound!.Id}"));

        await manager.ResumeInboundsAsync(GuestId, [inbound.Id]);

        Assert.Equal(1, _gateway.CallCount($"ResumeInbound:{inbound.Id}"));
    }

    [Fact]
    public async Task StartScreenShare_Twice_ReturnsTheSameOutbound()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        var (_, first, _) = await manager.StartScreenShareAsync(HostId, withAudio: false);
        var (_, second, _) = await manager.StartScreenShareAsync(HostId, withAudio: false);

        Assert.Equal(first, second);
        Assert.Equal(1, _gateway.CallCount($"AddOutbound:{HostId}"));
    }

    [Fact]
    public async Task ConnectPeer_ConsumesTheVoiceThatIsAlreadyShared_ButNotTheScreen()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);
        await manager.StartVoiceShareAsync(HostId);
        await manager.StartScreenShareAsync(HostId, withAudio: false);

        await JoinAndConnect(manager, GuestId);

        Assert.Equal(GuestId, Assert.Single(_events.Of<HybridSessionPeerStartedVoiceShareEvent>()).ListenerUserId);
        Assert.DoesNotContain(_events.Of<HybridSessionPeerStartedScreenShareEvent>(),
            started => started.ListenerUserId != null);
        Assert.Equal(1, _gateway.CallCount($"AddInbound:{GuestId}"));
        Assert.True(manager.GetAllPeerSnapshots()[HostId].IsScreenShareActive);
    }

    [Fact]
    public async Task StopScreenShare_RemovesTheOutboundAndKeepsTheVoice()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        var (_, voice) = await manager.StartVoiceShareAsync(HostId);
        var (_, screen, _) = await manager.StartScreenShareAsync(HostId, withAudio: false);

        var result = await manager.StopScreenShareAsync(HostId, HostId);

        Assert.Equal(StopScreenShareResult.NoError, result);
        Assert.Equal(1, _gateway.CallCount($"PauseOutbound:{screen!.Id}"));
        Assert.Equal(0, _gateway.CallCount($"RemoveOutbound:{screen.Id}"));
        Assert.Equal(0, _gateway.CallCount($"RemoveOutbound:{voice!.Id}"));
        Assert.Null(Assert.Single(_events.Of<HybridSessionPeerStoppedScreenShareEvent>()).InitiatorUserId);
        Assert.False(manager.GetAllPeerSnapshots()[HostId].IsScreenShareActive);
        Assert.Empty(_events.Of<HybridSessionPeerStoppedVoiceShareEvent>());
    }

    [Fact]
    public async Task StopScreenShare_ThenStart_ResumesTheSameOutbound_ButNotItsWatchers()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        var (_, first, _) = await manager.StartScreenShareAsync(HostId, withAudio: false);
        var (_, inbound, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);
        await manager.ResumeInboundsAsync(GuestId, [inbound!.Id]);
        await manager.StopScreenShareAsync(HostId, HostId);

        var (result, second, _) = await manager.StartScreenShareAsync(HostId, withAudio: false);

        Assert.Equal(StartScreenShareResult.NoError, result);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(1, _gateway.CallCount($"AddOutbound:{HostId}"));
        Assert.Equal(1, _gateway.CallCount($"ResumeOutbound:{first.Id}"));
        Assert.Equal(1, _gateway.CallCount($"PauseInbound:{inbound.Id}"));
        Assert.Equal(1, _gateway.CallCount($"ResumeInbound:{inbound.Id}"));
        Assert.True(manager.GetAllPeerSnapshots()[HostId].IsScreenShareActive);

        var (_, resumed, _) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        Assert.Equal(inbound, resumed);
        Assert.Equal(2, _gateway.CallCount($"ResumeInbound:{inbound.Id}"));
        Assert.Equal(1, _gateway.CallCount($"AddInbound:{GuestId}"));
    }

    [Fact]
    public async Task StartScreenShare_WithAudio_PublishesThePictureAndTheSound()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        var (result, outbound, audioOutbound) = await manager.StartScreenShareAsync(HostId, withAudio: true);

        Assert.Equal(StartScreenShareResult.NoError, result);
        Assert.Equal((MediaSourceType.Screen, MediaType.Video), (outbound!.SourceType, outbound.MediaType));
        Assert.Equal((MediaSourceType.Screen, MediaType.Audio), (audioOutbound!.SourceType, audioOutbound.MediaType));
        Assert.True(Assert.Single(_events.Of<HybridSessionPeerStartedScreenShareEvent>()).HasAudio);
        Assert.True(manager.GetAllPeerSnapshots()[HostId].IsScreenAudioActive);
        Assert.Equal(0, _gateway.CallCount($"AddInbound:{GuestId}"));
    }

    [Fact]
    public async Task StartScreenShare_WithoutAudio_PublishesNoSound()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);

        var (_, _, audioOutbound) = await manager.StartScreenShareAsync(HostId, withAudio: false);
        var (_, _, audioInbound) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        Assert.Null(audioOutbound);
        Assert.Null(audioInbound);
        Assert.False(Assert.Single(_events.Of<HybridSessionPeerStartedScreenShareEvent>()).HasAudio);
        Assert.False(manager.GetAllPeerSnapshots()[HostId].IsScreenAudioActive);
        Assert.Equal(1, _gateway.CallCount($"AddOutbound:{HostId}"));
    }

    [Fact]
    public async Task WatchScreenShare_WithAudio_ReceivesThePictureAndTheSound()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId, OtherGuestId);
        var (_, outbound, audioOutbound) = await manager.StartScreenShareAsync(HostId, withAudio: true);

        var (result, inbound, audioInbound) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        Assert.Equal(SetScreenShareWatchResult.NoError, result);
        Assert.Equal(outbound!.SSRC, inbound!.SSRC);
        Assert.Equal(audioOutbound!.SSRC, audioInbound!.SSRC);
        Assert.Equal(2, _gateway.CallCount($"AddInbound:{GuestId}"));
        Assert.Equal(0, _gateway.CallCount($"AddInbound:{OtherGuestId}"));
    }

    [Fact]
    public async Task UnwatchScreenShare_WithAudio_PausesThePictureAndTheSound()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        await manager.StartScreenShareAsync(HostId, withAudio: true);
        var (_, inbound, audioInbound) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);
        await manager.ResumeInboundsAsync(GuestId, [inbound!.Id, audioInbound!.Id]);

        var (result, watched, heard) = await manager.SetScreenShareWatchAsync(GuestId, HostId, false);
        await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        Assert.Equal(SetScreenShareWatchResult.NoError, result);
        Assert.Equal((inbound, audioInbound), (watched, heard));
        Assert.Equal(1, _gateway.CallCount($"PauseInbound:{audioInbound.Id}"));
        Assert.Equal(2, _gateway.CallCount($"ResumeInbound:{audioInbound.Id}"));
        Assert.Equal(2, _gateway.CallCount($"AddInbound:{GuestId}"));
    }

    [Fact]
    public async Task ResumeInbounds_OfTheScreenSound_ThatIsNoLongerWatched_KeepsItPaused()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        await manager.StartScreenShareAsync(HostId, withAudio: true);
        var (_, _, audioInbound) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);
        await manager.SetScreenShareWatchAsync(GuestId, HostId, false);

        await manager.ResumeInboundsAsync(GuestId, [audioInbound!.Id]);

        Assert.Equal(0, _gateway.CallCount($"ResumeInbound:{audioInbound.Id}"));
    }

    [Fact]
    public async Task StopScreenShare_WithAudio_PausesBothOutbounds_AndTheirWatchers()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        var (_, outbound, audioOutbound) = await manager.StartScreenShareAsync(HostId, withAudio: true);
        var (_, _, audioInbound) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        var result = await manager.StopScreenShareAsync(HostId, HostId);

        Assert.Equal(StopScreenShareResult.NoError, result);
        Assert.Equal(1, _gateway.CallCount($"PauseOutbound:{outbound!.Id}"));
        Assert.Equal(1, _gateway.CallCount($"PauseOutbound:{audioOutbound!.Id}"));
        Assert.Equal(1, _gateway.CallCount($"PauseInbound:{audioInbound!.Id}"));
        Assert.False(manager.GetAllPeerSnapshots()[HostId].IsScreenAudioActive);
    }

    [Fact]
    public async Task StartScreenShare_AgainWithAudio_ResumesTheSound_AndAgainWithout_LeavesItPaused()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        var (_, _, first) = await manager.StartScreenShareAsync(HostId, withAudio: true);
        await manager.StopScreenShareAsync(HostId, HostId);

        var (_, _, resumed) = await manager.StartScreenShareAsync(HostId, withAudio: true);
        await manager.StopScreenShareAsync(HostId, HostId);
        var (_, _, silent) = await manager.StartScreenShareAsync(HostId, withAudio: false);
        var (_, _, audioInbound) = await manager.SetScreenShareWatchAsync(GuestId, HostId, true);

        Assert.Equal(first!.Id, resumed!.Id);
        Assert.Null(silent);
        Assert.Null(audioInbound);
        Assert.Equal(1, _gateway.CallCount($"ResumeOutbound:{first.Id}"));
        Assert.Equal(2, _gateway.CallCount($"PauseOutbound:{first.Id}"));
        Assert.Equal(2, _gateway.CallCount($"AddOutbound:{HostId}"));
        Assert.Equal(1, _gateway.CallCount($"AddInbound:{GuestId}"));
    }

    [Fact]
    public async Task RemovePeer_RemovesTheScreenSound()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        var (_, _, audioOutbound) = await manager.StartScreenShareAsync(GuestId, withAudio: true);

        await manager.RemovePeerAsync(GuestId);

        Assert.Equal(1, _gateway.CallCount($"RemoveOutbound:{audioOutbound!.Id}"));
    }

    [Fact]
    public async Task RemovePeer_RemovesAStoppedScreenShare()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId);
        var (_, screen, _) = await manager.StartScreenShareAsync(GuestId, withAudio: false);
        await manager.StopScreenShareAsync(GuestId, GuestId);

        await manager.RemovePeerAsync(GuestId);

        Assert.Equal(1, _gateway.CallCount($"RemoveOutbound:{screen!.Id}"));
    }

    [Fact]
    public async Task RemovePeer_ReleasesTheInboundsItConsumed()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId, GuestId, OtherGuestId);
        await manager.StartVoiceShareAsync(HostId);
        await manager.StartVoiceShareAsync(OtherGuestId);

        await manager.RemovePeerAsync(GuestId);

        Assert.Equal(2, _gateway.Calls.Count(call => call.StartsWith($"RemoveInbound:in-{GuestId}-")));
    }

    [Fact]
    public async Task SetMicrophoneMuted_WithoutVoice_ReturnsVoiceNotStarted()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);

        Assert.Equal(SetMicrophoneMuteResult.VoiceNotStarted, await manager.SetMicrophoneMutedAsync(HostId, HostId, true));
    }

    [Fact]
    public async Task SetMicrophoneMuted_PausesTheOutboundOnce()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);
        var (_, outbound) = await manager.StartVoiceShareAsync(HostId);

        Assert.Equal(SetMicrophoneMuteResult.NoError, await manager.SetMicrophoneMutedAsync(GuestId, HostId, true));
        Assert.Equal(SetMicrophoneMuteResult.NoError, await manager.SetMicrophoneMutedAsync(GuestId, HostId, true));

        Assert.Equal(1, _gateway.CallCount($"PauseOutbound:{outbound!.Id}"));
        Assert.True(manager.GetAllPeerSnapshots()[HostId].IsMicrophoneMuted);
        var muted = Assert.Single(_events.Of<HybridSessionPeerMuteMicrophoneEvent>());
        Assert.Equal(GuestId, muted.InitiatorUserId);
    }

    [Fact]
    public async Task SetSoundMuted_IsKeptAndAnnouncedOnce()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);

        Assert.Equal(SetSoundMuteResult.NoError, await manager.SetSoundMutedAsync(HostId, HostId, true));
        Assert.Equal(SetSoundMuteResult.NoError, await manager.SetSoundMutedAsync(HostId, HostId, true));

        Assert.True(manager.GetAllPeerSnapshots()[HostId].IsSoundMuted);
        Assert.Single(_events.Of<HybridSessionPeerMuteSoundEvent>());

        Assert.Equal(SetSoundMuteResult.NoError, await manager.SetSoundMutedAsync(HostId, HostId, false));

        Assert.False(manager.GetAllPeerSnapshots()[HostId].IsSoundMuted);
        Assert.Single(_events.Of<HybridSessionPeerUnmuteSoundEvent>());
    }

    [Fact]
    public async Task SetAfk_KeepsTheReasonAndAnnouncesEachChange()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);

        Assert.Equal(SetAfkResult.NoError, await manager.SetAfkAsync(HostId, true, "  lunch "));
        Assert.Equal(SetAfkResult.NoError, await manager.SetAfkAsync(HostId, true, "lunch"));

        var peer = manager.GetAllPeerSnapshots()[HostId];
        Assert.True(peer.IsAfk);
        Assert.Equal("lunch", peer.AfkReason);
        Assert.Equal("lunch", Assert.Single(_events.Of<HybridSessionPeerAfkEvent>()).Reason);

        Assert.Equal(SetAfkResult.NoError, await manager.SetAfkAsync(HostId, false, "ignored"));

        peer = manager.GetAllPeerSnapshots()[HostId];
        Assert.False(peer.IsAfk);
        Assert.Null(peer.AfkReason);
        Assert.Single(_events.Of<HybridSessionPeerNotAfkEvent>());
    }

    [Fact]
    public async Task SetAfk_OfAPeerNotInTheSession_IsRefused()
    {
        var manager = await CreateManager();

        Assert.Equal(SetAfkResult.PeerNotExists, await manager.SetAfkAsync(HostId, true, null));
    }

    [Fact]
    public async Task Poke_GoesToThePokedPeerWithTheMessage()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);
        await JoinAndConnect(manager, GuestId);

        Assert.Equal(PokeResult.NoError, await manager.PokeAsync(GuestId, HostId, " wake up "));

        var poked = Assert.Single(_events.Of<HybridSessionPeerPokedEvent>());
        Assert.Equal(HostId, poked.UserId);
        Assert.Equal(GuestId, poked.InitiatorUserId);
        Assert.Equal("wake up", poked.Message);
    }

    [Fact]
    public async Task Poke_OfOneselfOrOfAnAbsentPeer_IsRefused()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);

        Assert.Equal(PokeResult.CannotPokeSelf, await manager.PokeAsync(HostId, HostId, null));
        Assert.Equal(PokeResult.PeerNotExists, await manager.PokeAsync(HostId, GuestId, null));
        Assert.Equal(PokeResult.PeerNotExists, await manager.PokeAsync(GuestId, HostId, null));
        Assert.Empty(_events.Of<HybridSessionPeerPokedEvent>());
    }

    // Timeouts

    [Fact]
    public async Task SfuTimeout_IsReportedAsTimedOut_AndDoesNotBlockTheSession()
    {
        var manager = await CreateManager();
        _gateway.TimesOut = true;

        var (result, _) = await manager.AddPeerAsync(HostId, "epoch");
        Assert.Equal(HybridSessionJoinResult.TimedOut, result);
        Assert.False(manager.HasPeer(HostId));

        _gateway.TimesOut = false;
        var joinAgain = manager.AddPeerAsync(HostId, "epoch");

        Assert.Same(joinAgain, await Task.WhenAny(joinAgain, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Equal(HybridSessionJoinResult.NoError, (await joinAgain).result);
    }

    [Fact]
    public async Task SfuTimeout_WhileStartingVoice_IsReportedAsTimedOut()
    {
        var manager = await CreateManager();
        await JoinAndConnect(manager, HostId);
        _gateway.TimesOut = true;

        var (result, outbound) = await manager.StartVoiceShareAsync(HostId);

        Assert.Equal(StartVoiceShareResult.TimedOut, result);
        Assert.Null(outbound);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(10);
        }
    }
}
