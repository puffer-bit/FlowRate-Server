using Microsoft.Extensions.Logging;
using Shared.Models.Notifications.CoordinatorInfo;
using Server.Core.Models.Coordinator;
using Server.Application.Mappers;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Heartbeat;
using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Users;
using Server.Application.Orchestrator.SFUPool;

namespace Server.Application.Coordinator.Settings;

/// <summary>
/// Hands new settings to the parts of a running coordinator that copied them on creation, then tells the users.
/// The listening endpoint is not changed here: that needs a new gateway, so the orchestrator restarts the coordinator.
/// </summary>
public class CoordinatorSettingsApplier
{
    private readonly ILogger _logger;
    private readonly ICoordinatorInstanceContext _context;
    private readonly IHeartbeatManager _heartbeatManager;
    private readonly IMediaService? _mediaService;
    private readonly CoordinatorSfuPool _sfuPool;
    private readonly IMessageSender _messageSender;
    private readonly IUserManager _userManager;
    private readonly RequestRateLimits _rateLimits;

    public CoordinatorSettingsApplier(
        ILogger logger,
        ICoordinatorInstanceContext context,
        IHeartbeatManager heartbeatManager,
        CoordinatorSfuPool sfuPool,
        IMessageSender messageSender,
        IUserManager userManager,
        RequestRateLimits rateLimits,
        IMediaService? mediaService = null)
    {
        _logger = logger;
        _context = context;
        _heartbeatManager = heartbeatManager;
        _mediaService = mediaService;
        _sfuPool = sfuPool;
        _messageSender = messageSender;
        _userManager = userManager;
        _rateLimits = rateLimits;
    }

    public void Apply(CoordinatorInstanceModel model)
    {
        // The server password and the name are read from the context on every use
        _context.ApplySettings(model);

        _heartbeatManager.ApplySettings(new HeartbeatSettings(model.Heartbeat.Interval, model.Heartbeat.Timeout));
        _mediaService?.SetLimits(model.Media);
        _mediaService?.SetUploadSettings(model.Uploads);
        // Connections and the gateway make their buckets anew from the new limits
        _rateLimits.Apply(model.RateLimits);
        // Sessions on a node that left the pool move to another one
        _sfuPool.SetNodes(model.SfuNodeIds);

        _logger.LogInformation("Coordinator {Name} reconfigured.", model.Name);

        _messageSender.BroadcastMessage(new CoordinatorReconfiguredNotification(_context.ToDto(
            _userManager.ConnectedUsersCount)));
    }
}
