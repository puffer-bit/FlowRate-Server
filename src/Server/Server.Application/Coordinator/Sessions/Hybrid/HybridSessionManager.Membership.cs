using Shared.Enums;
using Shared.Models.Media.Transport;

namespace Server.Application.Coordinator.Sessions.Hybrid;

public partial class HybridSessionManager
{
    public async Task<(ErrorCode? result, TransportData? transportData)> JoinPeerAsync(
        int sessionId, int userId, string clientEpoch, string? connectionId = null,
        bool pastCapacity = false, bool mayApprove = false)
    {
        // Calls, channel joins and moderator moves must not acquire media membership concurrently.
        await _callsLock.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return (ErrorCode.SessionNotFound, null);
            if (session.Model.CallDevices is { } devices)
            {
                // A call can have ended or selected another device since request authorization.
                if (!devices.TryGetValue(userId, out var selected) || selected != connectionId)
                    return (ErrorCode.NotMember, null);
            }
            else
            {
                if (!session.PeerManager.HasPeer(userId) && !pastCapacity
                    && session.Model.IsFull(session.PeerManager.GetAllPeerSnapshots().Count))
                    return (ErrorCode.SessionFull, null);
                var error = await LeaveOtherMediaAsync(userId, null, sessionId);
                if (error != null) return (error, null);
            }
            return await session.PeerManager.AddPeerAsync(userId, clientEpoch, connectionId, pastCapacity, mayApprove);
        }
        finally { _callsLock.Release(); }
    }

    private Task<ErrorCode?> ReleaseMediaPeerAsync(IHybridSession session, int userId) =>
        session.PeerManager.RemoveReplacedPeerAsync(userId);
}
