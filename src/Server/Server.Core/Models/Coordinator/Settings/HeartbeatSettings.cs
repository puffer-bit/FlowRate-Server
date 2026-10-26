namespace Server.Core.Models.Coordinator.Settings;

public sealed record CoordinatorHeartbeatSettings
{
    public int IntervalSeconds { get; }
    public int TimeoutSeconds { get; }

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    public CoordinatorHeartbeatSettings(int intervalSeconds, int timeoutSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(intervalSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(timeoutSeconds);

        IntervalSeconds = intervalSeconds;
        TimeoutSeconds = timeoutSeconds;
    }
}
