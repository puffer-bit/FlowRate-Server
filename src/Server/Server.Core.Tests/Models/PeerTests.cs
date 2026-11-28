using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Enums.WebRTC;

namespace Server.Core.Tests.Models;

public class PeerTests
{
    private const int ModeratorId = 1;
    private const int GuestId = 2;

    private static HybridSessionModel Session(bool isApprovalRequired) =>
        HybridSessionModel.Create("coordinator", "room", 10, WebRTCEngine.Mediasoup, isApprovalRequired);

    [Fact]
    public void WhoMayApprove_IsAdmittedWithoutApproval()
    {
        var moderator = Peer.Join(ModeratorId, Session(isApprovalRequired: true), mayApprove: true);

        Assert.True(moderator.IsAdmitted);
        Assert.Equal(HybridSessionPeerState.Connecting, moderator.State);
    }

    [Fact]
    public void Guest_WaitsForApproval_WhenSessionRequiresIt()
    {
        var guest = Peer.Join(GuestId, Session(isApprovalRequired: true));

        Assert.False(guest.IsAdmitted);
        Assert.Equal(HybridSessionPeerState.WaitingForApprove, guest.State);
    }

    [Fact]
    public void PendingGuest_DoesNotReceiveMedia_UntilApproved()
    {
        var guest = Peer.Join(GuestId, Session(isApprovalRequired: true));

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
        var guest = Peer.Join(GuestId, Session(isApprovalRequired: true));

        guest.Approve();

        Assert.Equal(HybridSessionPeerState.Connecting, guest.State);
        Assert.False(guest.CanReceiveMedia);
    }

    [Fact]
    public void Rejected_Peer_CannotBeApproved_RejoinOrConnect()
    {
        var guest = Peer.Join(GuestId, Session(isApprovalRequired: true));

        Assert.Equal(null, guest.Reject());

        Assert.Equal(HybridSessionPeerState.Closed, guest.State);
        Assert.Equal(ErrorCode.AlreadyRejected, guest.Approve());
        Assert.Equal(ErrorCode.NotAdmitted, guest.Rejoin());
        Assert.Equal(ErrorCode.NotAdmitted, guest.MarkTransportConnected());
    }

    [Fact]
    public void Approved_Peer_CannotBeRejected()
    {
        var guest = Peer.Join(GuestId, Session(isApprovalRequired: true));
        guest.Approve();

        Assert.Equal(ErrorCode.AlreadyApproved, guest.Reject());
        Assert.False(guest.IsRejected);
    }

    [Fact]
    public void Rejoin_ResetsTransportAndMicrophone_AndKeepsAdmission()
    {
        var guest = Peer.Join(GuestId, Session(isApprovalRequired: false));
        guest.MarkTransportConnected();
        guest.SetMicrophoneMuted(true);

        Assert.Equal(null, guest.Rejoin());

        Assert.False(guest.IsTransportConnected);
        Assert.True(guest.IsMicrophoneMuted);
        Assert.Equal(HybridSessionPeerState.Connecting, guest.State);
    }

    [Fact]
    public void WaitForServer_DoesNotReopenClosedPeer()
    {
        var guest = Peer.Join(GuestId, Session(isApprovalRequired: false));
        guest.Close();

        guest.WaitForServer();

        Assert.Equal(HybridSessionPeerState.Closed, guest.State);
    }

    [Fact]
    public void SetMicrophoneMuted_ReportsWhetherStateChanged()
    {
        var guest = Peer.Join(GuestId, Session(isApprovalRequired: false));

        Assert.True(guest.IsMicrophoneMuted);
        Assert.False(guest.SetMicrophoneMuted(true));
        Assert.True(guest.SetMicrophoneMuted(false));
        Assert.True(guest.SetMicrophoneMuted(true));
        Assert.False(guest.SetMicrophoneMuted(true));
    }
}
