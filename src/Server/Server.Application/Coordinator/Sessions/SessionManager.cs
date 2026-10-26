using System.Diagnostics.CodeAnalysis;
using Server.Core.Models;
using Shared.Enums;
using Shared.Permissions;
using Server.Application.Abstractions.Persistence;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Layout;

namespace Server.Application.Coordinator.Sessions;

public class SessionManager : ISessionManager
{
    private readonly IChatSessionManager _chatSessions;
    private readonly IHybridSessionManager _hybridSessions;
    private readonly ISessionLayoutService _layout;
    private readonly ISessionPermissionsRepository _permissions;
    private readonly string _coordinatorInstanceId;
    // Overrides are changed one session at a time, so the saved ones and those in memory stay the same
    private readonly SemaphoreSlim _permissionsLock = new(1, 1);

    public SessionManager(IChatSessionManager chatSessions, IHybridSessionManager hybridSessions,
        ISessionLayoutService layout, ISessionPermissionsRepository permissions, string coordinatorInstanceId)
    {
        _permissions = permissions;
        _coordinatorInstanceId = coordinatorInstanceId;
        _chatSessions = chatSessions;
        _hybridSessions = hybridSessions;
        _layout = layout;
    }

    public async Task LoadSessionsAsync()
    {
        await _chatSessions.LoadAsync();
        await _hybridSessions.LoadAsync();

        var overrides = await _permissions.GetByCoordinatorAsync(_coordinatorInstanceId);
        foreach (var session in GetUnorderedSessionModels())
            if (overrides.TryGetValue(session.Id, out var sessionOverrides))
                session.SetPermissionOverrides(sessionOverrides);
        await _layout.LoadAsync(GetUnorderedSessionModels().Select(session => session.Id));
    }

    public async Task<(CreateSessionResult result, SessionModel? session)> CreateSessionAsync(
        CreateSessionModel request, int userId)
    {
        (CreateSessionResult result, SessionModel? session) created;
        switch (request.SessionData.SessionType)
        {
            case SessionType.Chat:
                created = await _chatSessions.CreateAsync(request, userId);
                break;
            case SessionType.Hybrid:
                created = await _hybridSessions.CreateAsync(request, userId);
                break;
            default:
                return (CreateSessionResult.UnexpectedParameters, null);
        }

        if (created.session != null)
            await _layout.AddSessionAsync(created.session.Id, request.CategoryId);

        return created;
    }

    public async Task<DeleteSessionResult> DeleteSessionAsync(int sessionId, int userId)
    {
        DeleteSessionResult result;
        if (_chatSessions.TryGetSession(sessionId, out _))
            result = await _chatSessions.DeleteAsync(sessionId, userId);
        else if (_hybridSessions.TryGetSession(sessionId, out _))
            result = await _hybridSessions.DeleteAsync(sessionId, userId);
        else
            return DeleteSessionResult.SessionNotExists;

        if (result == DeleteSessionResult.NoError)
            await _layout.RemoveSessionAsync(sessionId);

        return result;
    }

    public async Task<ChangeSessionPermissionsResult> SetPermissionsAsync(int sessionId,
        IReadOnlyList<PermissionOverride> overrides)
    {
        await _permissionsLock.WaitAsync();
        try
        {
            if (!TryGetSessionModel(sessionId, out var session))
                return ChangeSessionPermissionsResult.SessionNotExists;

            await _permissions.ReplaceAsync(sessionId, overrides);
            session.SetPermissionOverrides(overrides);
            return ChangeSessionPermissionsResult.NoError;
        }
        finally
        {
            _permissionsLock.Release();
        }
    }

    public bool TryGetSessionModel(int sessionId, [NotNullWhen(true)] out SessionModel? session)
    {
        if (_chatSessions.TryGetSession(sessionId, out var chat))
        {
            session = chat;
            return true;
        }

        if (_hybridSessions.TryGetSession(sessionId, out var hybrid))
        {
            session = hybrid.Model;
            return true;
        }

        session = null;
        return false;
    }

    public IReadOnlyCollection<SessionModel> GetAllSessionModels()
    {
        var positions = _layout.GetOrder()
            .Select((sessionId, index) => (sessionId, index))
            .ToDictionary(entry => entry.sessionId, entry => entry.index);

        return GetUnorderedSessionModels()
            .OrderBy(session => positions.GetValueOrDefault(session.Id, int.MaxValue))
            .ToList();
    }

    private IEnumerable<SessionModel> GetUnorderedSessionModels() =>
        _chatSessions.GetAllSessions().OrderBy(session => session.Id)
            .Concat<SessionModel>(_hybridSessions.GetAllSessions().Select(session => session.Model)
                .OrderBy(session => session.Id));

    public Task RemoveUserFromAllSessionsAsync(int userId) => _hybridSessions.RemoveUserFromAllSessionsAsync(userId);

    public Task RemoveConnectionFromAllSessionsAsync(int userId, string connectionId) =>
        _hybridSessions.RemoveConnectionFromAllSessionsAsync(userId, connectionId);

    public Task CloseAllSessionsAsync() => _hybridSessions.CloseAllSessionsAsync();

    public void Dispose()
    {
        _hybridSessions.Dispose();
        _permissionsLock.Dispose();
    }
}
