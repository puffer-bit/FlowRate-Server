using System.Diagnostics.CodeAnalysis;
using Server.Core.Models;
using Shared.Enums;
using Shared.Permissions;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Events;

namespace Server.Application.Coordinator.Sessions;

/// <summary>
/// Operations that apply to every session type. Type specific work goes to
/// <see cref="IChatSessionManager"/> and <see cref="IHybridSessionManager"/>.
/// </summary>
public interface ISessionManager : IDisposable
{
    Task LoadSessionsAsync();

    Task<(CreateSessionResult result, SessionModel? session)> CreateSessionAsync(CreateSessionModel request, int userId);
    Task<DeleteSessionResult> DeleteSessionAsync(int sessionId, int userId);

    /// <summary>Saves the permission overrides of the session in place of the ones it has.</summary>
    Task<ChangeSessionPermissionsResult> SetPermissionsAsync(int sessionId, IReadOnlyList<PermissionOverride> overrides);

    bool TryGetSessionModel(int sessionId, [NotNullWhen(true)] out SessionModel? session);
    IReadOnlyCollection<SessionModel> GetAllSessionModels();

    Task RemoveUserFromAllSessionsAsync(int userId);
    /// <summary>Removes the user from the sessions they take part in from this connection only.</summary>
    Task RemoveConnectionFromAllSessionsAsync(int userId, string connectionId);
    Task CloseAllSessionsAsync();
}
