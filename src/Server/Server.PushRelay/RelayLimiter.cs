using System.Threading.RateLimiting;

namespace Server.PushRelay;

public sealed class RelayLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _addresses;
    private readonly PartitionedRateLimiter<string> _tokens;

    public RelayLimiter(RelayOptions options)
    {
        _addresses = Create(options.RequestsPerMinutePerAddress);
        _tokens = Create(options.RequestsPerMinutePerToken);
    }

    public bool TryAcquireAddress(string address) => Acquire(_addresses, address);

    public bool TryAcquireToken(string token) => Acquire(_tokens, token);

    private static bool Acquire(PartitionedRateLimiter<string> limiter, string key)
    {
        using var lease = limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    private static PartitionedRateLimiter<string> Create(int perMinute) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetTokenBucketLimiter(key,
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = perMinute,
                TokensPerPeriod = perMinute,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    public void Dispose()
    {
        _addresses.Dispose();
        _tokens.Dispose();
    }
}
