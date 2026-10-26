using System.Collections.Concurrent;
using Server.Core.Enums;
using Server.Core.Models.Coordinator;
using Server.Application.Coordinator;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Snapshots;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Authentication;
using Server.Application.Abstractions.Sfu;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Orchestrator.Settings;

namespace Server.Application.Orchestrator {
    public interface IOrchestratorInstance
    {
        IOrchestratorInstanceContext Context { get; init; }
        ConcurrentDictionary<string, ICoordinatorInstance> CoordinatorsPool { get; init; }
        bool IsGatewayReady { get; set; }
        bool IsReady { get; set; }
        bool IsStarted { get; set; }
        TimeSpan UpTime => DateTime.UtcNow - Context.CurrentLaunchTime;
        bool IsHardwareAccelerated { get; set; }

        // Live totals over the coordinators in the pool
        int ActiveSessionsCount => CoordinatorsPool.Values.Sum(coordinator => coordinator.ActiveSessionsCount);
        int ConnectedUsersCount => CoordinatorsPool.Values.Sum(coordinator => coordinator.ConnectedUsersCount);
        
        Task Configure();
        
        // Lifecycle actions
        Task StartAsync(CancellationToken ct = default);
        Task StopAsync(CancellationToken ct = default);
        Task RestartAsync(CancellationToken ct = default);
        Task EnterMaintenanceModeAsync();

        // DB Maintenance
        Task CheckDatabaseStateAsync();
        Task MigrateToNewDatabaseAsync(string targetConnectionString);
        Task BackupDatabaseAsync(string backupPath);
        Task RestoreDatabaseBackupAsync(string backupFilePath);

        // Media Maintenance
        Task BackupMediaAsync(string targetPath);
        Task RestoreMediaBackupAsync(string sourcePath);

        // Server init
        Task CollectRegionDataAsync();
        Task CollectHostSystemDataAsync();
        void SetLaunchTime(DateTime utcTime);

        // Health and statistics
        //Task<ServerHealthStatus> GetHealthStatusAsync();
        //Task<ServerMetrics> GetCurrentMetricsAsync();

        // CoordinatorInstances actions
        ICoordinatorInstance? GetCoordinator(string coordinatorId);
        IReadOnlyList<CoordinatorSnapshot> GetCoordinatorSnapshots();
        Task<CoordinatorInstanceModel> CreateCoordinatorInstanceAsync(string name, string ip, int port);
        Task AttachCoordinatorInstance(CoordinatorInstanceModel coordinatorInstanceEntity);
        Task DetachCoordinatorInstanceAsync(string coordinatorInstanceId);
        Task DisableCoordinatorInstanceAsync(string coordinatorInstanceId);
        Task EnableCoordinatorInstanceAsync(string coordinatorInstanceId);
        Task CommitCoordinatorDataAsync();
        Task RemoveCoordinatorInstanceAsync(string coordinatorInstanceId);
        Task RestoreCoordinatorInstanceAsync(string coordinatorInstanceId);
        Task PurgeCoordinatorInstanceAsync(string coordinatorInstanceId);

        // Connection actions
        void EnableRemoteConnections();
        void RestrictRemoteConnections(bool applyToActiveConnections);
        void DisableRemoteConnections();
        Task AddAllowedIpAsync(string ip);

        // Other
        Task ProcessExternalAction();
        void CreatePfxCert();

        // Events
        event EventHandler<ServerStateChangedEventArgs> StateChanged;

        /// <summary>A coordinator was attached to the pool, started, failed to start or was detached.</summary>
        event Action<string>? CoordinatorChanged;
    }

    public class ServerStateChangedEventArgs : EventArgs
    {
        public ServerState PreviousState { get; }
        public ServerState CurrentState { get; }
        public DateTime ChangedAtUtc { get; }
        public string? TriggeredBy { get; }

        public ServerStateChangedEventArgs(
            ServerState previousState,
            ServerState currentState,
            DateTime changedAtUtc,
            string? triggeredBy = null)
        {
            PreviousState = previousState;
            CurrentState = currentState;
            ChangedAtUtc = changedAtUtc;
            TriggeredBy = triggeredBy;
        }
    }
}
