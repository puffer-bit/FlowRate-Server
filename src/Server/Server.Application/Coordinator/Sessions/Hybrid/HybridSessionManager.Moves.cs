using Microsoft.Extensions.Logging;
using Shared.Enums;

namespace Server.Application.Coordinator.Sessions.Hybrid;

public partial class HybridSessionManager
{
    /// <summary>How long a place is kept for a moved peer whose client has not joined the target yet.</summary>
    public static readonly TimeSpan MovedPeerJoinTimeout = TimeSpan.FromSeconds(30);

    public async Task<ErrorCode?> MovePeerAsync(int initiatorId, int sessionId, int userId, int targetSessionId)
    {
        if (initiatorId == userId)
            return ErrorCode.CannotTargetSelf;
        if (sessionId == targetSessionId)
            return ErrorCode.InvalidValue;
        if (!_sessions.TryGetValue(sessionId, out var source) || !_sessions.TryGetValue(targetSessionId, out var target))
            return ErrorCode.SessionNotFound;
        // A direct call is not a channel: nobody is moved into or out of one
        if (source.Model.CallDevices != null || target.Model.CallDevices != null)
            return ErrorCode.WrongSessionType;

        // Only a user who takes part in the session can be moved, never one who is in no session
        if (!source.PeerManager.TryGetPeerConnection(userId, out var connectionId))
            return ErrorCode.PeerNotFound;

        // The place in the target comes first, so that the peer is never left out of both sessions.
        // The two sessions are never locked together.
        var result = await target.PeerManager.MoveInPeerAsync(initiatorId, userId, connectionId, sessionId);
        if (result != null)
            return result;

        result = await source.PeerManager.MoveOutPeerAsync(initiatorId, userId, connectionId, targetSessionId);
        if (result != null)
        {
            // The peer left or joined from another device meanwhile
            await target.PeerManager.RemoveUnclaimedPeerAsync(userId);
            return result;
        }

        _logger.LogInformation("User {InitiatorId} moved user {UserId} from hybrid session {SessionId} to {TargetSessionId}.",
            initiatorId, userId, sessionId, targetSessionId);
        _ = GiveUpUnclaimedPlaceAsync(target, userId);
        return null;
    }

    private async Task GiveUpUnclaimedPlaceAsync(IHybridSession session, int userId)
    {
        try
        {
            await Task.Delay(MovedPeerJoinTimeout, _time, _lifetime.Token);
            if (await session.PeerManager.RemoveUnclaimedPeerAsync(userId) == null)
                _logger.LogInformation("User {UserId} did not join hybrid session {SessionId} after being moved there.",
                    userId, session.Model.Id);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Giving up the place of moved user {UserId} in hybrid session {SessionId} failed.",
                userId, session.Model.Id);
        }
    }
}
