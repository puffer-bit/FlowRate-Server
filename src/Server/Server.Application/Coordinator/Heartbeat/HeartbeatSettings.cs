namespace Server.Application.Coordinator.Heartbeat;

/// <param name="Interval">How often the client is pinged.</param>
/// <param name="Timeout">How long without a pong before the connection is dropped.</param>
public record HeartbeatSettings(TimeSpan Interval, TimeSpan Timeout);
