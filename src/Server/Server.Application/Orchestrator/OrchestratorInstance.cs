using System.Net;
using Server.Application.Abstractions.Gateway;
using Server.Core.Models.Coordinator.Settings;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Server.Core.Enums;
using Server.Core.Models.Coordinator;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Orchestrator.Bootstrap;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Snapshots;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Authentication;
using Server.Application.Abstractions.Persistence;
using Server.Application.Abstractions.Factories;
using Server.Application.Orchestrator.Settings;

namespace Server.Application.Orchestrator
{
    public class OrchestratorInstance : IOrchestratorInstance
    {
        public required IOrchestratorInstanceContext Context { get; init; }
        public required ConcurrentDictionary<string, ICoordinatorInstance> CoordinatorsPool { get; init; } = new();
        public bool IsGatewayReady { get; set; }
        public bool IsReady { get; set; }
        public bool IsStarted { get; set; }
        public bool IsHardwareAccelerated { get; set; }
        public bool IsRemoteConnectionsAvailable { get; set; }
        public bool IsRemoteConnectionsRestricted { get; set; }
        public bool IsTestSessionsAllowed { get; set; }
        public ServerState State { get; set; }

        private readonly ILogger _logger;
        
        private readonly IOrchestratorInstanceRepository _orchestratorInstanceRepository;
        private readonly ICoordinatorSettingsStore _coordinatorSettingsStore;
        private readonly ICoordinatorInstanceFactory _coordinatorInstanceFactory;
        private readonly ConfigurationSeeder _configurationSeeder;
        private readonly ISfuNodeRegistry _sfuNodeRegistry;
        // Settings changes are applied one at a time, so a restart never overlaps the next change
        private readonly SemaphoreSlim _settingsChangeLock = new(1, 1);
        private readonly SemaphoreSlim _creationLock = new(1, 1);
        private readonly ConcurrentDictionary<string, CoordinatorSnapshot> _failedCoordinators = new();
        private readonly IListenEndpointProbe _endpointProbe;
        private readonly ISfuNodeStore _sfuNodeStore;
        public event EventHandler<ServerStateChangedEventArgs>? StateChanged;
        public event Action<string>? CoordinatorChanged;
        
        public OrchestratorInstance(IOrchestratorInstanceContext orchestratorInstanceContext,
            ILoggerFactory loggerFactory,
            ICoordinatorInstanceFactory coordinatorInstanceFactory, 
            IOrchestratorInstanceRepository orchestratorInstanceRepository,
            ICoordinatorSettingsStore coordinatorSettingsStore,
            ConfigurationSeeder configurationSeeder,
            ISfuNodeRegistry sfuNodeRegistry,
            IListenEndpointProbe endpointProbe, ISfuNodeStore sfuNodeStore)
        {
            State = ServerState.Starting;
            Context = orchestratorInstanceContext;
            _configurationSeeder = configurationSeeder;
            _logger = loggerFactory.CreateLogger($"Orchestrator");
            _orchestratorInstanceRepository = orchestratorInstanceRepository;
            _coordinatorSettingsStore = coordinatorSettingsStore;
            _coordinatorInstanceFactory = coordinatorInstanceFactory;
            _sfuNodeRegistry = sfuNodeRegistry;
            _endpointProbe = endpointProbe;
            _sfuNodeStore = sfuNodeStore;
        }

        public async Task Configure()
        {
            _logger.LogTrace("Configuring orchestrator...");

            var orchestratorConfig = await _orchestratorInstanceRepository.GetAsync();
            if (orchestratorConfig == null)
            {
                _logger.LogTrace("Database not contains config. Creating...");
                
                Context.CreateContext();
            }
            else
            {
                Context.UpdateContext(orchestratorConfig);
            }
            
            await _orchestratorInstanceRepository.SaveOrUpdateAsync(Context.AsModel());
            await _configurationSeeder.SeedAsync();
        }
        
        public async Task StartAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Starting orchestrator...");

            // Nodes must exist before coordinators load their sessions; connecting can go on in the background
            await _sfuNodeRegistry.LoadAsync(ct);
            _ = ConnectSfuNodesAsync();

            var (started, failed) = await StartCoordinatorsAsync(ct);
            if (started == 0 && failed > 0)
                throw new InvalidOperationException($"None of the {failed} coordinators could be started.");

            _coordinatorSettingsStore.Changed += OnCoordinatorSettingsChangedAsync;

            State = ServerState.Online;
            if (failed > 0)
                _logger.LogWarning("Orchestrator started, but {Failed} of {Total} coordinators failed to start.",
                    failed, started + failed);
            else
                _logger.LogInformation("Orchestrator successfully started.");
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            if (State is ServerState.Closing or ServerState.Offline)
                return;

            State = ServerState.Closing;
            _logger.LogInformation("Closing orchestrator...");

            _coordinatorSettingsStore.Changed -= OnCoordinatorSettingsChangedAsync;
            // Waits for a change being applied, so it does not start a coordinator after they were all closed
            await _settingsChangeLock.WaitAsync(ct);
            _settingsChangeLock.Release();

            foreach (var coordinatorId in CoordinatorsPool.Keys)
            {
                await DetachCoordinatorInstanceAsync(coordinatorId);
            }

            State = ServerState.Offline;
            _logger.LogInformation("Orchestrator closed.");
        }

        public Task RestartAsync(CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task EnterMaintenanceModeAsync()
        {
            throw new NotImplementedException();
        }

        public Task CheckDatabaseStateAsync()
        {
            throw new NotImplementedException();
        }

        public Task MigrateToNewDatabaseAsync(string targetConnectionString)
        {
            throw new NotImplementedException();
        }

        public Task BackupDatabaseAsync(string backupPath)
        {
            throw new NotImplementedException();
        }

        public Task RestoreDatabaseBackupAsync(string backupFilePath)
        {
            throw new NotImplementedException();
        }

        public Task BackupMediaAsync(string targetPath)
        {
            throw new NotImplementedException();
        }

        public Task RestoreMediaBackupAsync(string sourcePath)
        {
            throw new NotImplementedException();
        }

        public Task CollectRegionDataAsync()
        {
            throw new NotImplementedException();
        }

        public Task CollectHostSystemDataAsync()
        {
            throw new NotImplementedException();
        }

        public void SetLaunchTime(DateTime utcTime)
        {
            throw new NotImplementedException();
        }

        public ICoordinatorInstance? GetCoordinator(string coordinatorId)
        {
            CoordinatorsPool.TryGetValue(coordinatorId, out var coordinatorInstance);
            return coordinatorInstance;
        }

        public IReadOnlyList<CoordinatorSnapshot> GetCoordinatorSnapshots() =>
            _failedCoordinators.Values
                .Concat(CoordinatorsPool.Values.Select(coordinator => coordinator.GetSnapshot()))
                .GroupBy(snapshot => snapshot.Id).Select(group => group.Last())
                .OrderBy(snapshot => snapshot.Name)
                .ToList();
        
        public async Task<CoordinatorCreationResult> CreateCoordinatorInstanceAsync(CreateCoordinator request,
            CancellationToken ct = default)
        {
            // A second creation must see the first one's saved configuration and listener.
            await _creationLock.WaitAsync(ct);
            try
            {
                if (State != ServerState.Online)
                    return new(CoordinatorCreationStatus.Unavailable);

                var coordinators = await _coordinatorSettingsStore.GetAllAsync(ct);
                if (coordinators.Count >= Context.MaxCoordinators)
                    return new(CoordinatorCreationStatus.LimitReached);

                var change = request.ToSettingsChange();
                var nodes = await _sfuNodeStore.GetAllAsync(ct);
                var errors = CoordinatorSettingsValidator.Validate(change, nodes.Select(node => node.Id).ToHashSet());
                if (errors.Count > 0)
                    return new(CoordinatorCreationStatus.Invalid, Errors: errors);

                var address = IPAddress.Parse(change.Ip);
                if (!_endpointProbe.IsLocalAddress(address))
                    return Invalid(CoordinatorSettingsFields.Ip, $"This machine has no address {change.Ip}.");

                // Reserve configured endpoints too, including coordinators that failed to start.
                if (coordinators.Any(model => model.Port == change.Port && EndpointsOverlap(model.Ip, address))
                    || !_endpointProbe.IsPortFree(address, change.Port))
                    return Invalid(CoordinatorSettingsFields.Port, $"Port {change.Port} is already taken on {change.Ip}.");

                var model = CoordinatorInstanceModel.Create(change.Name, change.Ip, change.Port, DateTime.UtcNow);
                model.SetUserLimits(change.MaxOnlineUsers, change.MaxUsers);
                model.SetHeartbeat(new CoordinatorHeartbeatSettings(change.HeartbeatIntervalSeconds,
                    change.HeartbeatTimeoutSeconds));
                model.AssignSfuNodes(change.SfuNodeIds);
                // The store change starts the new coordinator through the same path as a settings update.
                // Finish this operation even if the browser disconnects after the configuration was saved.
                await _coordinatorSettingsStore.AddAsync(model, ct);
                var state = GetCoordinator(model.Id)?.State ?? CoordinatorState.Failed;
                return new(CoordinatorCreationStatus.Created, model, state);
            }
            finally
            {
                _creationLock.Release();
            }
        }

        private static CoordinatorCreationResult Invalid(string field, string message) =>
            new(CoordinatorCreationStatus.Invalid, Errors: new Dictionary<string, string[]> { [field] = [message] });

        private static bool EndpointsOverlap(string existingIp, IPAddress address)
        {
            var existing = IPAddress.Parse(existingIp);
            return existing.Equals(address) || existing.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Any)
                || (existing.AddressFamily == address.AddressFamily
                    && (existing.Equals(IPAddress.Any) || address.Equals(IPAddress.Any)));
        }

        public async Task AttachCoordinatorInstance(CoordinatorInstanceModel coordinatorInstanceModel)
        {
            ICoordinatorInstance? coordinatorInstance = null;
            var attached = false;
            try
            {
                coordinatorInstance = _coordinatorInstanceFactory.CreateCoordinatorInstance(
                    Context.ServerVersion, coordinatorInstanceModel);
                if (!CoordinatorsPool.TryAdd(coordinatorInstanceModel.Id, coordinatorInstance))
                {
                    coordinatorInstance.Dispose();
                    return;
                }
                attached = true;
                _failedCoordinators.TryRemove(coordinatorInstanceModel.Id, out _);
                CoordinatorChanged?.Invoke(coordinatorInstanceModel.Id);
                await coordinatorInstance.StartAsync();
            }
            catch
            {
                if (attached)
                    CoordinatorsPool.TryRemove(coordinatorInstanceModel.Id, out _);
                _failedCoordinators[coordinatorInstanceModel.Id] = new CoordinatorSnapshot(
                    coordinatorInstanceModel.Id, coordinatorInstanceModel.Name, coordinatorInstanceModel.Ip,
                    coordinatorInstanceModel.Port, CoordinatorState.Failed, coordinatorInstanceModel.CurrentLaunchTime,
                    [], []);
                coordinatorInstance?.Dispose();
                throw;
            }
            finally
            {
                CoordinatorChanged?.Invoke(coordinatorInstanceModel.Id);
            }
        }

        public async Task DetachCoordinatorInstanceAsync(string coordinatorInstanceId)
        {
            if (!CoordinatorsPool.TryRemove(coordinatorInstanceId, out var coordinatorInstance))
                return;

            try
            {
                await coordinatorInstance.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Closing coordinator {Id} failed.", coordinatorInstanceId);
            }
            finally
            {
                coordinatorInstance.Dispose();
                CoordinatorChanged?.Invoke(coordinatorInstanceId);
            }
        }

        public async Task DisableCoordinatorInstanceAsync(string coordinatorInstanceId)
        {
            throw new NotImplementedException();
        }

        public async Task EnableCoordinatorInstanceAsync(string coordinatorInstanceId)
        {
            throw new NotImplementedException();
        }

        public async Task CommitCoordinatorDataAsync()
        {
            throw new NotImplementedException();
        }

        public async Task RemoveCoordinatorInstanceAsync(string coordinatorInstanceId)
        {
            throw new NotImplementedException();
        }

        public async Task RestoreCoordinatorInstanceAsync(string coordinatorInstanceId)
        {
            throw new NotImplementedException();
        }

        public async Task PurgeCoordinatorInstanceAsync(string coordinatorInstanceId)
        {
            throw new NotImplementedException();
        }

        public void EnableRemoteConnections()
        {
            throw new NotImplementedException();
        }

        public void RestrictRemoteConnections(bool applyToActiveConnections)
        {
            throw new NotImplementedException();
        }

        public void DisableRemoteConnections()
        {
            throw new NotImplementedException();
        }

        public async Task AddAllowedIpAsync(string ip)
        {
            throw new NotImplementedException();
        }

        public async Task ProcessExternalAction()
        {
            throw new NotImplementedException();
        }
        
        private void MigrateDb()
        {
            
        }
        
        public void CreatePfxCert()
        {

        }
        
        private async Task<(int Started, int Failed)> StartCoordinatorsAsync(CancellationToken ct)
        {
            _logger.LogTrace("Fetching coordinators from database...");

            var started = 0;
            var failed = 0;
            foreach (var coordinator in await _coordinatorSettingsStore.GetAllAsync(ct))
            {
                try
                {
                    await AttachCoordinatorInstance(coordinator);
                    started++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Coordinator {Name} ({Id}) on {Ip}:{Port} failed to start. Exception: {Message}",
                        coordinator.Name, coordinator.Id, coordinator.Ip, coordinator.Port, ex);
                }
            }

            return (started, failed);
        }

        private async Task OnCoordinatorSettingsChangedAsync(string coordinatorId)
        {
            await _settingsChangeLock.WaitAsync();
            try
            {
                if (State is ServerState.Closing or ServerState.Offline)
                    return;

                var model = await _coordinatorSettingsStore.GetAsync(coordinatorId);
                if (model == null)
                    return;

                var coordinator = GetCoordinator(coordinatorId);
                if (coordinator == null)
                {
                    // A new coordinator, or one that could not start: the new settings may let it
                    _logger.LogInformation("Starting coordinator {Name} ({Id}) with the changed settings...",
                        model.Name, model.Id);
                    await AttachCoordinatorInstance(model);
                    return;
                }

                var current = coordinator.Context.Model;
                if (current.Ip != model.Ip || current.Port != model.Port)
                {
                    // Not awaited: a change a user sent comes over a connection of this coordinator, and closing it
                    // waits for that very request, which would hold the answer until the gateway gives up
                    _ = Task.Run(() => RestartAsync(current, model));
                    return;
                }

                coordinator.ApplySettings(model);
            }
            catch (Exception ex)
            {
                // The settings are saved already; whoever changed them sees the state of the coordinator in the panel
                _logger.LogError(ex, "Applying the changed settings of coordinator {Id} failed.", coordinatorId);
            }
            finally
            {
                _settingsChangeLock.Release();
            }
        }

        /// <summary>
        /// Moves a coordinator to its new address. When it cannot listen there, it goes back to the old one, so a
        /// wrong address never leaves it stopped.
        /// </summary>
        private async Task RestartAsync(CoordinatorInstanceModel current, CoordinatorInstanceModel model)
        {
            await _settingsChangeLock.WaitAsync();
            try
            {
                if (State is ServerState.Closing or ServerState.Offline)
                    return;

                _logger.LogInformation("Coordinator {Name} moves from {OldIp}:{OldPort} to {Ip}:{Port}, restarting...",
                    model.Name, current.Ip, current.Port, model.Ip, model.Port);
                await DetachCoordinatorInstanceAsync(model.Id);

                try
                {
                    await AttachCoordinatorInstance(model);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Coordinator {Name} cannot listen on {Ip}:{Port}, it stays on {OldIp}:{OldPort}.",
                        model.Name, model.Ip, model.Port, current.Ip, current.Port);
                }

                await AttachCoordinatorInstance(current);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restarting coordinator {Id} with the changed settings failed.", model.Id);
            }
            finally
            {
                _settingsChangeLock.Release();
            }
        }

        private async Task ConnectSfuNodesAsync()
        {
            try
            {
                await _sfuNodeRegistry.ConnectAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connecting to SFU nodes failed.");
            }
        }
    }
}
