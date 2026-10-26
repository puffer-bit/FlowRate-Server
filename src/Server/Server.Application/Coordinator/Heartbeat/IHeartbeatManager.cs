namespace Server.Application.Coordinator.Heartbeat;

public interface IHeartbeatManager
{
    event Action<string>? TimeoutOccured;

    Task StartAsync(string connectionId);
    void RegisterHeartbeat(string connectionId);

    /// <summary>Takes effect from the next check of the connections.</summary>
    void ApplySettings(HeartbeatSettings settings);
}
