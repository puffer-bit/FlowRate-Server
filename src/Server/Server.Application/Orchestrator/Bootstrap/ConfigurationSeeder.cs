using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Application.Options;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Orchestrator;
using Server.Application.Orchestrator.Settings;

namespace Server.Application.Orchestrator.Bootstrap;

/// <summary>
/// Writes the SFU next to the server and the base coordinator to the database once, when it is empty.
/// After that the database is the only source of truth.
/// </summary>
public class ConfigurationSeeder
{
    private const string LocalSfuName = "local";
    private const int LocalSfuPort = 50051;

    private const string DefaultCoordinatorName = "Default server (No, is not)";
    private const string DefaultCoordinatorIp = "0.0.0.0";
    private const int DefaultCoordinatorPort = 26666;
    private const int DefaultMaxOnlineUsers = 64;
    private const int DefaultMaxUsers = 256;
    private const int DefaultHeartbeatIntervalSeconds = 5;
    private const int DefaultHeartbeatTimeoutSeconds = 40;

    private readonly OrchestratorOptions _orchestratorOptions;
    private readonly ISfuNodeStore _sfuNodeStore;
    private readonly ICoordinatorSettingsStore _coordinatorSettingsStore;
    private readonly ILogger _logger;

    public ConfigurationSeeder(
        IOptions<OrchestratorOptions> orchestratorOptions,
        ISfuNodeStore sfuNodeStore,
        ICoordinatorSettingsStore coordinatorSettingsStore,
        ILoggerFactory loggerFactory)
    {
        _orchestratorOptions = orchestratorOptions.Value;
        _sfuNodeStore = sfuNodeStore;
        _coordinatorSettingsStore = coordinatorSettingsStore;
        _logger = loggerFactory.CreateLogger("Orchestrator");
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var nodes = await SeedSfuNodesAsync(ct);
        await SeedDefaultCoordinatorAsync(nodes, ct);
    }

    private async Task<IReadOnlyCollection<SfuNodeSettingsModel>> SeedSfuNodesAsync(CancellationToken ct)
    {
        var nodes = await _sfuNodeStore.GetAllAsync(ct);
        if (nodes.Count > 0)
            return nodes;

        var node = new SfuNodeSettingsModel
        {
            Name = LocalSfuName,
            Host = _orchestratorOptions.LocalSfuHost,
            Port = LocalSfuPort,
            // The SFU next to the server takes no credentials
            Username = string.Empty,
            Key = string.Empty
        };

        await _sfuNodeStore.AddAsync(node, ct);
        _logger.LogInformation("SFU node {Name} ({Host}:{Port}) added.", node.Name, node.Host, node.Port);

        return await _sfuNodeStore.GetAllAsync(ct);
    }

    private async Task SeedDefaultCoordinatorAsync(IReadOnlyCollection<SfuNodeSettingsModel> nodes, CancellationToken ct)
    {
        var coordinators = await _coordinatorSettingsStore.GetAllAsync(ct);
        if (coordinators.Count > 0)
            return;

        var model = CoordinatorInstanceModel.Create(DefaultCoordinatorName, DefaultCoordinatorIp,
            DefaultCoordinatorPort, DateTime.UtcNow, isDefault: true);
        model.SetUserLimits(DefaultMaxOnlineUsers, DefaultMaxUsers);
        model.SetHeartbeat(new CoordinatorHeartbeatSettings(DefaultHeartbeatIntervalSeconds,
            DefaultHeartbeatTimeoutSeconds));
        model.AssignSfuNodes(nodes.Select(node => node.Id));

        await _coordinatorSettingsStore.AddAsync(model, ct);
        _logger.LogInformation("Default coordinator {Name} created.", model.Name);
    }
}
