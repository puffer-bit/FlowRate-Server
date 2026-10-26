namespace Server.Core.Models.Coordinator.Settings;

/// <summary>At most <see cref="Count"/> requests per <see cref="PeriodSeconds"/>.</summary>
public sealed record RequestRateSetting
{
    public int Count { get; }
    public double PeriodSeconds { get; }

    public RequestRateSetting(int count, double periodSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodSeconds);

        Count = count;
        PeriodSeconds = periodSeconds;
    }
}

/// <summary>How many requests and connections a coordinator takes from one client.</summary>
public sealed record CoordinatorRateLimitSettings
{
    public required RequestRateSetting Global { get; init; }
    public required RequestRateSetting AuthenticationPerIp { get; init; }
    public required RequestRateSetting SessionManagement { get; init; }
    public required RequestRateSetting SessionLayout { get; init; }
    public required RequestRateSetting SessionMedia { get; init; }
    public required RequestRateSetting MicrophoneMute { get; init; }
    public required RequestRateSetting InboundResume { get; init; }
    public required RequestRateSetting Moderation { get; init; }
    public required RequestRateSetting ChatWrite { get; init; }
    public required RequestRateSetting ChatRead { get; init; }
    public required RequestRateSetting PushRegistration { get; init; }
    public required RequestRateSetting MediaToken { get; init; }
    public required RequestRateSetting Query { get; init; }
    public required RequestRateSetting Heartbeat { get; init; }

    /// <summary>A connection that has this many requests rejected within the window is closed.</summary>
    public int MaxRejectionsPerWindow { get; init; } = 20;
    public int RejectionWindowSeconds { get; init; } = 60;

    /// <summary>
    /// How many client connections one IP address may keep open. Raise it when the clients reach the server
    /// through a proxy or a shared address.
    /// </summary>
    public int MaxConnectionsPerIp { get; init; } = 10;

    public static CoordinatorRateLimitSettings Default { get; } = new()
    {
        Global = new(300, 3),
        AuthenticationPerIp = new(5, 60),
        SessionManagement = new(10, 60),
        SessionLayout = new(20, 10),
        SessionMedia = new(10, 5),
        MicrophoneMute = new(10, 5),
        InboundResume = new(30, 5),
        Moderation = new(10, 10),
        ChatWrite = new(10, 10),
        ChatRead = new(20, 4),
        PushRegistration = new(10, 5),
        MediaToken = new(10, 10),
        Query = new(200, 4),
        Heartbeat = new(4, 2)
    };
}
