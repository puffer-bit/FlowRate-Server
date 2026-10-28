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

    Task<(ErrorCode? result, HybridSessionModel? session)> CreateAsync(CreateSessionModel request, int userId);
    Task<ErrorCode?> DeleteAsync(int sessionId, int userId);

    /// <summary>Replaces the settings of the session, saves them and tells every client.</summary>
    Task<ErrorCode?> ChangeSettingsAsync(int sessionId, string name, int capacity,
        bool isHostMustApprove, HybridMediaSettings media, int userId);
    bool TryGetSession(int sessionId, [NotNullWhen(true)] out IHybridSession? session);
    IReadOnlyCollection<IHybridSession> GetAllSessions();

    Task RemoveUserFromAllSessionsAsync(int userId);
    /// <summary>Removes the user from the sessions they take part in from this connection only.</summary>
    Task RemoveConnectionFromAllSessionsAsync(int userId, string connectionId);
    Task CloseAllSessionsAsync();
}
