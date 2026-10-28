using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Enums.WebRTC;

namespace Server.Core.Tests.Models;

public class PeerTests
{
    private const int HostId = 1;
    private const int GuestId = 2;

    private static HybridSessionModel Session(bool isHostMustApprove) =>
        HybridSessionModel.Create("coordinator", "room", 10, HostId, WebRTCEngine.Mediasoup, isHostMustApprove);

    [Fact]
    public void Host_IsAdmittedWithoutApproval()
    {
        var host = Peer.Join(HostId, Session(isHostMustApprove: true));

        Assert.True(host.IsHost);
        Assert.True(host.IsAdmitted);
        Assert.Equal(HybridSessionPeerState.Connecting, host.State);
    }

    [Fact]
    public void Guest_WaitsForApproval_WhenSessionRequiresIt()
    {
        var guest = Peer.Join(GuestId, Session(isHostMustApprove: true));

        Assert.False(guest.IsAdmitted);
        Assert.Equal(HybridSessionPeerState.WaitingForApprove, guest.State);
    }

    [Fact]
    public void PendingGuest_DoesNotReceiveMedia_UntilApproved()
    {
        var guest = Peer.Join(GuestId, Session(isHostMustApprove: true));

        Assert.Equal(null, guest.MarkTransportConnected());
        Assert.False(guest.CanReceiveMedia);
        Assert.Equal(HybridSessionPeerState.WaitingForApprove, guest.State);

        Assert.Equal(null, guest.Approve());
        Assert.True(guest.IsApproved);
        Assert.True(guest.CanReceiveMedia);
    }

    [Fact]
    public void Approve_BeforeTransport_LeavesPeerConnecting()
    {
        var guest = Peer.Join(GuestId, Session(isHostMustApprove: true));

        guest.Approve();

        Assert.Equal(HybridSessionPeerState.Connecting, guest.State);
        Assert.False(guest.CanReceiveMedia);
    }

    [Fact]
    public void Host_CannotBeApprovedOrRejected()
    {
        var host = Peer.Join(HostId, Session(isHostMustApprove: true));

        Assert.Equal(ErrorCode.Internal, host.Approve());
        Assert.Equal(ErrorCode.Internal, host.Reject());
    }

    [Fact]
    public void Rejected_Peer_CannotBeApproved_RejoinOrConnect()
    {
        var guest = Peer.Join(GuestId, Session(isHostMustApprove: true));

        Assert.Equal(null, guest.Reject());

        Assert.Equal(HybridSessionPeerState.Closed, guest.State);
        Assert.Equal(ErrorCode.AlreadyRejected, guest.Approve());
        Assert.Equal(ErrorCode.NotAdmitted, guest.Rejoin());
        Assert.Equal(ErrorCode.NotAdmitted, guest.MarkTransportConnected());
    }

    [Fact]
    public void Approved_Peer_CannotBeRejected()
    {
        var guest = Peer.Join(GuestId, Session(isHostMustApprove: true));
        guest.Approve();

        Assert.Equal(ErrorCode.AlreadyApproved, guest.Reject());
        Assert.False(guest.IsRejected);
    }

    [Fact]
    public void Rejoin_ResetsTransportAndMicrophone_AndKeepsAdmission()
    {
        var guest = Peer.Join(GuestId, Session(isHostMustApprove: false));
        guest.MarkTransportConnected();
        guest.SetMicrophoneMuted(true);

        Assert.Equal(null, guest.Rejoin());

        Assert.False(guest.IsTransportConnected);
        Assert.False(guest.IsMicrophoneMuted);
        Assert.Equal(HybridSessionPeerState.Connecting, guest.State);
    }

    [Fact]
    public void WaitForServer_DoesNotReopenClosedPeer()
    {
        var guest = Peer.Join(GuestId, Session(isHostMustApprove: false));
        guest.Close();

        guest.WaitForServer();

        Assert.Equal(HybridSessionPeerState.Closed, guest.State);
    }

    [Fact]
    public void SetMicrophoneMuted_ReportsWhetherStateChanged()
    {
        var guest = Peer.Join(GuestId, Session(isHostMustApprove: false));

        Assert.True(guest.SetMicrophoneMuted(true));
        Assert.False(guest.SetMicrophoneMuted(true));
    }
}
