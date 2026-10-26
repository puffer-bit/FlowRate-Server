using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Events;
using Server.Core.Models.HybridSession;
using Shared.Enums;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Shared.Models.Requests.SessionActions.Generic.Models.SessionData;
using Server.Application.Abstractions.Persistence;
using Server.Application.Mappers;
using Server.Application.Abstractions.Factories;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;
using Server.Application.Orchestrator;
using Server.Application.Orchestrator.SFUPool;

namespace Server.Application.Coordinator.Sessions.Hybrid;

public class HybridSessionManager : IHybridSessionManager
{
    private readonly ILogger _logger;
    private readonly ICoordinatorEventBus _coordinatorEventBus;
    private readonly IVideoSessionFactory _sessionFactory;
    private readonly ISFUNodeSelector _sfuNodeSelector;
    private readonly IVideoSessionRepository _sessionRepository;
    private readonly string _coordinatorInstanceId;

    private readonly ConcurrentDictionary<int, IHybridSession> _sessions = new();

    // Serializes creation, deletion and changes, so that two sessions cannot take the same name
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    public HybridSessionManager(
        ILogger logger,
        ICoordinatorEventBus coordinatorEventBus,
        IVideoSessionFactory sessionFactory,
        ISFUNodeSelector sfuNodeSelector,
        IVideoSessionRepository sessionRepository,
        string coordinatorInstanceId)
    {
        _logger = logger;
        _coordinatorEventBus = coordinatorEventBus;
        _sessionFactory = sessionFactory;
        _sfuNodeSelector = sfuNodeSelector;
        _sessionRepository = sessionRepository;
        _coordinatorInstanceId = coordinatorInstanceId;

        _sfuNodeSelector.NodesChanged += HandleSfuNodesChangedAsync;
    }

    public async Task LoadAsync()
    {
        foreach (var model in await _sessionRepository.GetByCoordinatorIdAsync(_coordinatorInstanceId))
        {
            try
            {
                _sessions.TryAdd(model.Id, await CreateSessionAsync(model));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hybrid session {SessionId} ({Name}) could not be loaded and is skipped.",
                    model.Id, model.Name);
            }
        }
    }

    public async Task<(CreateSessionResult result, HybridSessionModel? session)> CreateAsync(CreateSessionModel request,
        int userId)
    {
        if (request.Capacity <= 1)
            return (CreateSessionResult.WrongCapacity, null);

        if (request.SessionData is not HybridSessionCreateData hybridData)
            return (CreateSessionResult.UnexpectedParameters, null);

        await _lifecycleLock.WaitAsync();
        try
        {
            if (_sessions.Values.Any(s => s.Model.Name == request.Name))
            {
                _logger.LogTrace("Failed to create hybrid session. Name \"{Name}\" already used", request.Name);
                return (CreateSessionResult.NameAlreadyUsed, null);
            }

            var model = HybridSessionModel.Create(_coordinatorInstanceId, request.Name, request.Capacity, userId,
                hybridData.EngineType, media: hybridData.Media?.ToModel());
            model.Id = await _sessionRepository.AddAsync(model);

            try
            {
                _sessions[model.Id] = await CreateSessionAsync(model);
            }
            catch
            {
                await _sessionRepository.RemoveAsync(model.Id);
                throw;
            }

            _logger.LogTrace("Hybrid session \"{Name}\" (ID: {SessionId}) created", model.Name, model.Id);
            await _coordinatorEventBus.PublishAsync(new HybridSessionCreatedEvent(model, userId));
            return (CreateSessionResult.NoError, model);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create hybrid session.");
            return (CreateSessionResult.InternalError, null);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<DeleteSessionResult> DeleteAsync(int sessionId, int userId)
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (!_sessions.TryRemove(sessionId, out var session))
                return DeleteSessionResult.SessionNotExists;

            try
            {
                await session.CloseSession();
            }
            finally
            {
                session.Dispose();
            }

            await _sessionRepository.RemoveAsync(sessionId);

            _logger.LogTrace("Hybrid session \"{Name}\" (ID: {SessionId}) deleted", session.Model.Name, sessionId);
            await _coordinatorEventBus.PublishAsync(new HybridSessionDeletedEvent(sessionId, userId));
            return DeleteSessionResult.NoError;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete hybrid session {SessionId}.", sessionId);
            return DeleteSessionResult.InternalError;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<ChangeSessionSettingsResult> ChangeSettingsAsync(int sessionId, string name, int capacity,
        bool isHostMustApprove, HybridMediaSettings media, int userId)
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return ChangeSessionSettingsResult.SessionNotExists;

            // Same rule as on creation, and the peers already inside must still fit
            if (capacity <= 1 || capacity < session.PeerManager.GetAllPeerSnapshots().Count)
                return ChangeSessionSettingsResult.WrongCapacity;

            if (_sessions.Values.Any(s => s.Model.Id != sessionId && s.Model.Name == name))
            {
                _logger.LogTrace("Failed to change hybrid session {SessionId}. Name \"{Name}\" already used",
                    sessionId, name);
                return ChangeSessionSettingsResult.NameAlreadyUsed;
            }

            var model = session.Model;
            var (oldName, oldCapacity, oldIsHostMustApprove, oldMedia) =
                (model.Name, model.Capacity, model.IsHostMustApprove, model.Media);

            model.Rename(name);
            model.ChangeCapacity(capacity);
            model.SetHostMustApprove(isHostMustApprove);
            model.SetMedia(media);

            try
            {
                await _sessionRepository.UpdateAsync(model);
            }
            catch
            {
                // The session in memory stays as it is saved
                model.Rename(oldName);
                model.ChangeCapacity(oldCapacity);
                model.SetHostMustApprove(oldIsHostMustApprove);
                model.SetMedia(oldMedia);
                throw;
            }

            _logger.LogTrace("Hybrid session \"{Name}\" (ID: {SessionId}) reconfigured", model.Name, sessionId);
            await _coordinatorEventBus.PublishAsync(new HybridSessionReconfiguredEvent(model, userId));
            return ChangeSessionSettingsResult.NoError;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to change hybrid session {SessionId}.", sessionId);
            return ChangeSessionSettingsResult.InternalError;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public bool TryGetSession(int sessionId, [NotNullWhen(true)] out IHybridSession? session) =>
        _sessions.TryGetValue(sessionId, out session);

    public IReadOnlyCollection<IHybridSession> GetAllSessions() => _sessions.Values.ToList();

    public async Task RemoveUserFromAllSessionsAsync(int userId)
    {
        foreach (var session in _sessions.Values)
        {
            if (!session.PeerManager.HasPeer(userId))
                continue;

            try
            {
                await session.PeerManager.RemovePeerAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Removing user {UserId} from hybrid session {SessionId} failed.",
                    userId, session.Model.Id);
            }
        }
    }

    public async Task RemoveConnectionFromAllSessionsAsync(int userId, string connectionId)
    {
        foreach (var session in _sessions.Values)
        {
            if (!session.PeerManager.HasPeer(userId))
                continue;

            try
            {
                await session.PeerManager.RemovePeerOfConnectionAsync(userId, connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Removing user {UserId} of connection {ConnectionId} from hybrid session {SessionId} failed.",
                    userId, connectionId, session.Model.Id);
            }
        }
    }

    public async Task CloseAllSessionsAsync()
    {
        foreach (var session in _sessions.Values)
        {
            try
            {
                await session.CloseSession();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Closing hybrid session {SessionId} failed.", session.Model.Id);
            }
        }
    }

    /// <summary>Moves sessions off nodes that were disabled, removed or unassigned and gives detached ones a node.</summary>
    private async Task HandleSfuNodesChangedAsync()
    {
        try
        {
            await _lifecycleLock.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            foreach (var session in _sessions.Values)
            {
                try
                {
                    await session.AssignSfuNodeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Moving hybrid session {SessionId} to another SFU node failed.",
                        session.Model.Id);
                }
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task<IHybridSession> CreateSessionAsync(HybridSessionModel model)
    {
        var session = _sessionFactory.CreateHybridSession(model, _coordinatorEventBus, _sfuNodeSelector);
        await session.AssignSfuNodeAsync();
        return session;
    }

    public void Dispose()
    {
        _sfuNodeSelector.NodesChanged -= HandleSfuNodesChangedAsync;

        foreach (var session in _sessions.Values)
            session.Dispose();

        _sessions.Clear();
        _lifecycleLock.Dispose();
    }
}
