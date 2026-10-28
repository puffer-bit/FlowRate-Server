using Shared.Enums;

namespace Server.Application.Abstractions.Sfu;

/// <summary>What the orchestrator last learned about its connection to one SFU node.</summary>
/// <param name="RoundTripTime">Round trip of the last answered ping; null while the node does not answer.</param>
/// <param name="LastPingAtUtc">When the last ping was answered.</param>
/// <param name="StateChangedAtUtc">When <paramref name="State"/> last changed.</param>
/// <param name="Error">Why the node is not connected, if known.</param>
public sealed record SfuNodeStatus(
    SFUNodeConnectionState State,
    TimeSpan? RoundTripTime,
    DateTimeOffset? LastPingAtUtc,
    DateTimeOffset StateChangedAtUtc,
    string? Error = null,
    SfuNodeLoad? Load = null)
{
    public static SfuNodeStatus Initial() => new(SFUNodeConnectionState.Connecting, null, null, DateTimeOffset.UtcNow);
}

/// <summary>Load the node reported with its last ping.</summary>
public sealed record SfuNodeLoad(int SessionCount, int WorkerCount, TimeSpan Uptime);
