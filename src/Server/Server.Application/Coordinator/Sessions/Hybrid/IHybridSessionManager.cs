using System.Diagnostics.CodeAnalysis;
using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Events;

namespace Server.Application.Coordinator.Sessions.Hybrid;

public interface IHybridSessionManager : IDisposable
{
    Task LoadAsync();
    /// <summary>Whether the user takes part in a chat call.</summary>
    bool IsInCall(int userId);
    /// <summary>Sends the running calls of the user's chats to a connection that has just loaded them.</summary>
    Task PublishCurrentCallAsync(int userId, string connectionId);
    /// <summary>The selected device leaves the call that runs in the media session.</summary>
    Task LeaveCallForSessionAsync(int sessionId, int userId, string connectionId);
    /// <summary>Takes the user out of every call, e.g. when they enter a voice channel.</summary>
    Task LeaveCallsAsync(int userId);
    /// <summary>The group owner, in the call on <paramref name="connectionId"/>, takes a member out of it.</summary>
    Task<ErrorCode?> KickFromCallAsync(int sessionId, int initiatorId, string connectionId, int userId, string? reason);
    /// <summary>Follows a change of a chat's members or owner, or its removal.</summary>
    Task OnChatChangedAsync(int chatId);
    Task<(ErrorCode? Error, Shared.Models.DataTransferObjects.ChatCallDTO? Call)> ChangeCallAsync(
        Shared.Models.Requests.SessionActions.ChatSession.ChatCallRequest request, string connectionId);
    /// <summary>
    /// Declines a call that rings for <paramref name="userId"/> without a connection, from the button of a
    /// push notification. A call that has already ended needs nothing and is no error.
    /// </summary>
    Task<ErrorCode?> DeclineCallFromPushAsync(Guid callId, int userId);

    Task<(ErrorCode? result, HybridSessionModel? session)> CreateAsync(CreateSessionModel request, int userId);
    Task<ErrorCode?> DeleteAsync(int sessionId, int userId);

    /// <summary>Replaces the settings of the session, saves them and tells every client.</summary>
    Task<ErrorCode?> ChangeSettingsAsync(int sessionId, string name, int capacity,
        bool isApprovalRequired, HybridMediaSettings media, int userId);
    bool TryGetSession(int sessionId, [NotNullWhen(true)] out IHybridSession? session);
    IReadOnlyCollection<IHybridSession> GetAllSessions();

    /// <summary>
    /// Moves a peer of <paramref name="sessionId"/> to <paramref name="targetSessionId"/>, past its capacity and
    /// approval. The target keeps a place for the peer until its client joins there, for
    /// <see cref="HybridSessionManager.MovedPeerJoinTimeout"/> at most. Permissions are the caller's to check.
    /// </summary>
    Task<ErrorCode?> MovePeerAsync(int initiatorId, int sessionId, int userId, int targetSessionId);

    Task RemoveUserFromAllSessionsAsync(int userId);
    /// <summary>Removes the user from the sessions they take part in from this connection only.</summary>
    Task RemoveConnectionFromAllSessionsAsync(int userId, string connectionId);
    Task CloseAllSessionsAsync();
}
