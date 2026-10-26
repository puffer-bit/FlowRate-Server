using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Notifications;
using Microsoft.Extensions.Logging;
using Server.Core.Enums;
using Server.Core.Models.Coordinator;
using Shared.Models.Notifications.Heartbeat;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Users;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Heartbeat;
using Server.Application.Coordinator.Sessions;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Snapshots;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Settings;
using Server.Application.Coordinator.Roles;

namespace Server.Application.Coordinator;

public partial class CoordinatorInstance : ICoordinatorInstance
{
    public ICoordinatorInstanceContext Context { get; }
    public CoordinatorState State { get; private set; } = CoordinatorState.Offline;

    private readonly ILogger _logger;

    private readonly ISessionManager _sessionManager;
    private readonly IHybridSessionManager _hybridSessionManager;
    private readonly IUserManager _userManager;
    private readonly IMessageSender _messageSender;
    private readonly IMessageRouter _messageRouter;
    private readonly IGatewayService _gatewayService;
    private readonly IConnectionManager _connectionManager;
    private readonly IHeartbeatManager _heartbeatManager;
    private readonly PushNotifier? _pushNotifier;
    private readonly IMediaService? _mediaService;
    private readonly CoordinatorSettingsApplier? _settingsApplier;
    private readonly CoordinatorRoles? _roles;

    public CoordinatorInstance(
        ICoordinatorInstanceContext coordinatorInstanceContext,
        ILogger logger,
        ISessionManager sessionManager,
        IHybridSessionManager hybridSessionManager,
        IUserManager userManager,
        IMessageSender messageSender,
        IMessageRouter messageRouter,
        IGatewayService gatewayService,
        IConnectionManager connectionManager,
        IHeartbeatManager heartbeatManager,
        PushNotifier? pushNotifier = null,
        IMediaService? mediaService = null,
        CoordinatorSettingsApplier? settingsApplier = null,
        CoordinatorRoles? roles = null)
    {
        _roles = roles;
        _settingsApplier = settingsApplier;
        _mediaService = mediaService;
        Context = coordinatorInstanceContext;
        _logger = logger;
        _sessionManager = sessionManager;
        _hybridSessionManager = hybridSessionManager;
        _userManager = userManager;
        _messageSender = messageSender;
        _messageRouter = messageRouter;
        _gatewayService = gatewayService;
        _connectionManager = connectionManager;
        _heartbeatManager = heartbeatManager;
        _pushNotifier = pushNotifier;

        SubscribeToEvents();
    }

    public int ConnectedUsersCount => _userManager.ConnectedUsersCount;

    public int ActiveSessionsCount =>
        _hybridSessionManager.GetAllSessions().Count(session => session.PeerManager.GetAllPeerSnapshots().Count > 0);

    public async Task StartAsync()
    {
        if (State != CoordinatorState.Offline)
            return;

        State = CoordinatorState.Starting;
        _logger.LogInformation("Coordinator {Name} starting...", Context.Name);

        try
        {
            // Requests are checked against the roles, so they are there before the first connection
            if (_roles != null)
                await _roles.LoadAsync();
            await _sessionManager.LoadSessionsAsync();
            await _gatewayService.StartAsync(_messageRouter);
            _mediaService?.Start();

            State = CoordinatorState.Online;
            _logger.LogInformation("Coordinator {Name} successful started.", Context.Name);
        }
        catch
        {
            await CloseAsync();
            State = CoordinatorState.Failed;
            throw;
        }
    }

    public async Task CloseAsync()
    {
        if (State is CoordinatorState.Closing or CoordinatorState.Closed)
            return;

        State = CoordinatorState.Closing;
        _logger.LogInformation("Coordinator {Name} closing...", Context.Name);

        foreach (var user in _userManager.GetAllConnectedUsers().Values)
        {
            RunCloseStep($"detaching user {user.Id}", () =>
            {
                _messageSender.SendMessageByUserId(user.Id, new HeartbeatServerClosingNotification(Context.Id));
                _userManager.DetachUser(user.Id, "Server shutting down");
            });
        }

        try
        {
            await _sessionManager.CloseAllSessionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coordinator {Name}: closing sessions failed.", Context.Name);
        }

        RunCloseStep("closing connections", _connectionManager.CloseAllConnections);
        try
        {
            await _gatewayService.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coordinator {Name}: stopping the gateway failed.", Context.Name);
        }

        State = CoordinatorState.Closed;
        _logger.LogInformation("Coordinator {Name} closed.", Context.Name);
    }

    public void ApplySettings(CoordinatorInstanceModel model)
    {
        if (_settingsApplier == null)
            throw new InvalidOperationException($"Coordinator {Context.Name} cannot apply settings while running.");

        _settingsApplier.Apply(model);
    }

    public CoordinatorSnapshot GetSnapshot()
    {
        var sessions = _hybridSessionManager.GetAllSessions()
            .Select(session => new HybridSessionSnapshot(
                session.Model.Id,
                session.Model.Name,
                session.Model.HostId,
                session.Model.Capacity,
                session.Model.EngineType,
                session.Model.IsHostMustApprove,
                session.Model.State,
                session.PeerManager.GetAllPeerSnapshots().Values.OrderBy(peer => peer.UserId).ToList()))
            .OrderBy(session => session.Id)
            .ToList();

        var users = _userManager.GetAllConnectedUsers().Values
            .Select(user => new ConnectedUserSnapshot(
                user.Id, user.Username, user.DisplayName, user.Status, user.LastConnectionTime))
            .OrderBy(user => user.Id)
            .ToList();

        var model = Context.Model;
        return new CoordinatorSnapshot(model.Id, model.Name, model.Ip, model.Port, State, model.CurrentLaunchTime, sessions, users);
    }

    private void RunCloseStep(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coordinator {Name}: {Step} failed.", Context.Name, step);
        }
    }

    public void Dispose()
    {
        UnsubscribeFromEvents();

        RunCloseStep("disposing the gateway", _gatewayService.Dispose);
        RunCloseStep("disposing the session manager", _sessionManager.Dispose);
        RunCloseStep("disposing the connection manager", _connectionManager.Dispose);
        RunCloseStep("disposing the message sender", _messageSender.Dispose);
        if (_pushNotifier != null)
            RunCloseStep("disposing the push notifier", _pushNotifier.Dispose);
        if (_mediaService != null)
            RunCloseStep("disposing the media service", _mediaService.Dispose);
        if (_roles != null)
            RunCloseStep("disposing the roles", _roles.Dispose);
    }
}
