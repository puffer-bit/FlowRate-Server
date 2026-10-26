using System.Collections.Concurrent;

namespace Server.Application.Coordinator.Connections.RateLimiting;

public sealed class AuthenticationAttemptLimiter
{
    private const int CleanupEvery = 256;

    private readonly TimeProvider _time;
    private readonly RequestRateLimits _limits;
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private int _calls;
    private int _bucketsVersion;

    public AuthenticationAttemptLimiter(TimeProvider time, RequestRateLimits? limits = null)
    {
        _time = time;
        _limits = limits ?? RequestRateLimits.Default;
        _bucketsVersion = _limits.Version;
    }

    public bool TryAcquire(string ip)
    {
        if (Interlocked.Increment(ref _calls) % CleanupEvery == 0)
            RemoveIdle();

        // Buckets made from older limits are dropped, so new limits start full
        var version = _limits.Version;
        if (Interlocked.Exchange(ref _bucketsVersion, version) != version)
            _buckets.Clear();

        var limit = _limits.AuthenticationPerIp;
        var bucket = _buckets.GetOrAdd(ip, _ => new TokenBucket(limit.Capacity, limit.RefillPerSecond, _time));
        return bucket.TryTake();
    }

    private void RemoveIdle()
    {
        foreach (var (ip, bucket) in _buckets)
        {
            if (bucket.IsFull)
                _buckets.TryRemove(new KeyValuePair<string, TokenBucket>(ip, bucket));
        }
    }
}
