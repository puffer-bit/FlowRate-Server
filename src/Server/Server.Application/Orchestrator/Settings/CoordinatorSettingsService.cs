using System.Net;
using Microsoft.Extensions.Logging;
using Server.Application.Abstractions.Gateway;
using Server.Application.Abstractions.Security;
using Server.Core.Models.Coordinator;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Orchestrator;

namespace Server.Application.Orchestrator.Settings;

public class CoordinatorSettingsService : ICoordinatorSettingsService
{
    public event Action<string>? SettingsChanged;

    private readonly ICoordinatorSettingsStore _store;
    private readonly ISfuNodeStore _sfuNodeStore;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IListenEndpointProbe _endpointProbe;
    private readonly ILogger<CoordinatorSettingsService> _logger;

    // The revision is checked and the change saved as one step, so of two changes on one revision only one wins
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public CoordinatorSettingsService(ICoordinatorSettingsStore store, ISfuNodeStore sfuNodeStore,
        IPasswordHasher passwordHasher, IListenEndpointProbe endpointProbe, ILogger<CoordinatorSettingsService> logger)
    {
        _endpointProbe = endpointProbe;
        _store = store;
        _sfuNodeStore = sfuNodeStore;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public Task<CoordinatorInstanceModel?> GetAsync(string coordinatorId, CancellationToken ct = default) =>
        _store.GetAsync(coordinatorId, ct);

    public async Task<IReadOnlyList<SfuNodeSettingsModel>> GetSfuNodesAsync(CancellationToken ct = default) =>
        (await _sfuNodeStore.GetAllAsync(ct)).OrderBy(node => node.Name).ToList();

    public async Task<CoordinatorSettingsUpdateResult> UpdateAsync(string coordinatorId,
        CoordinatorSettingsChange change, long expectedRevision, CancellationToken ct = default)
    {
        CoordinatorInstanceModel? model;
        await _updateLock.WaitAsync(ct);
        try
        {
            model = await _store.GetAsync(coordinatorId, ct);
            if (model == null)
                return CoordinatorSettingsUpdateResult.NotFound;

            if (model.Revision != expectedRevision)
                return new CoordinatorSettingsUpdateResult(CoordinatorSettingsUpdateStatus.Conflict, model,
                    new Dictionary<string, string[]>());

            var nodeIds = (await _sfuNodeStore.GetAllAsync(ct)).Select(node => node.Id).ToHashSet();
            var errors = CoordinatorSettingsValidator.Validate(change, nodeIds);
            if (errors.Count == 0)
                errors = CheckEndpoint(model, change);
            if (errors.Count > 0)
                return new CoordinatorSettingsUpdateResult(CoordinatorSettingsUpdateStatus.Invalid, model, errors);

            Apply(change, model);
            // Saving also applies the settings to the running coordinator, or restarts it for a new endpoint
            await _store.UpdateAsync(model, ct);
            _logger.LogInformation("Settings of coordinator {Name} ({Id}) changed, revision {Revision}.",
                model.Name, model.Id, model.Revision);
        }
        finally
        {
            _updateLock.Release();
        }

        SettingsChanged?.Invoke(coordinatorId);

        return new CoordinatorSettingsUpdateResult(CoordinatorSettingsUpdateStatus.Updated, model,
            new Dictionary<string, string[]>());
    }

    /// <summary>
    /// A new address or port restarts the coordinator, which would be left stopped if it could not listen there;
    /// so what can be told beforehand is refused before saving.
    /// </summary>
    private IReadOnlyDictionary<string, string[]> CheckEndpoint(CoordinatorInstanceModel model,
        CoordinatorSettingsChange change)
    {
        var ip = change.Ip.Trim();
        if (ip == model.Ip && change.Port == model.Port)
            return new Dictionary<string, string[]>();

        var address = IPAddress.Parse(ip);
        if (!_endpointProbe.IsLocalAddress(address))
            return new Dictionary<string, string[]>
            {
                [CoordinatorSettingsFields.Ip] = [$"This machine has no address {ip}."]
            };

        // The coordinator holds its own port until it restarts, so only another port is tried
        if (change.Port != model.Port && !_endpointProbe.IsPortFree(address, change.Port))
            return new Dictionary<string, string[]>
            {
                [CoordinatorSettingsFields.Port] = [$"Port {change.Port} is already taken on {ip}."]
            };

        return new Dictionary<string, string[]>();
    }

    private void Apply(CoordinatorSettingsChange change, CoordinatorInstanceModel model)
    {
        model.Rename(change.Name.Trim());
        model.Listen(change.Ip.Trim(), change.Port);
        model.SetUserLimits(change.MaxOnlineUsers, change.MaxUsers);
        model.SetMotd(new MotdSettings(change.IsMotdEnabled,
            string.IsNullOrWhiteSpace(change.MotdText) ? null : change.MotdText.Trim()));
        model.SetAccessList(new AccessListSettings(change.AllowedIps.Distinct(), change.BannedIps.Distinct()));
        model.SetHeartbeat(new CoordinatorHeartbeatSettings(change.HeartbeatIntervalSeconds,
            change.HeartbeatTimeoutSeconds));

        model.SetMedia(new CoordinatorMediaSettings(change.MaxFileSizeBytes, change.StorageQuotaBytes));
        model.SetUploads(new CoordinatorUploadSettings(change.MaxUnattachedUploadsPerUser,
            change.UnattachedUploadLifetimeHours, change.MediaTokenLifetimeMinutes));
        model.SetRateLimits(change.RateLimits.ToSettings());
        model.AssignSfuNodes(change.SfuNodeIds);

        if (change.RemovePassword)
            model.SetPassword(null);
        else if (change.NewPassword != null)
            model.SetPassword(_passwordHasher.HashPassword(change.NewPassword));
    }
}
