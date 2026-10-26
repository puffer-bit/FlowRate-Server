using Shared.Models;

namespace Server.Application.Coordinator.Connections.RateLimiting;

public enum RequestGuardVerdict
{
    Allowed,
    Rejected,
    RejectedTooOften
}

public sealed class ConnectionRequestGuard
{
    private readonly TimeProvider _time;
    private readonly RequestRateLimits _limits;
    private TokenBucket _global;
    private int _limitsVersion;
    private readonly Dictionary<RequestRateGroup, TokenBucket> _groups = new();
    private readonly Queue<long> _rejections = new();
    private readonly object _lock = new();

    public ConnectionRequestGuard(TimeProvider time, RequestRateLimits? limits = null)
    {
        _time = time;
        _limits = limits ?? RequestRateLimits.Default;
        _limitsVersion = _limits.Version;
        _global = NewBucket(_limits.Global);
    }

    public RequestGuardVerdict Check(BaseMessage message, out RequestRateGroup group)
    {
        group = RequestRateLimits.GroupOf(message);

        TokenBucket global;
        TokenBucket? bucket = null;
        lock (_lock)
        {
            // New limits were applied: the buckets start over from them
            var version = _limits.Version;
            if (version != _limitsVersion)
            {
                _limitsVersion = version;
                _global = NewBucket(_limits.Global);
                _groups.Clear();
            }

            global = _global;
            // Logins are limited per IP address by the authentication limiter
            if (group != RequestRateGroup.Authentication && !_groups.TryGetValue(group, out bucket))
            {
                bucket = NewBucket(_limits.For(group));
                _groups[group] = bucket;
            }
        }

        return global.TryTake() && (bucket == null || bucket.TryTake())
            ? RequestGuardVerdict.Allowed
            : RegisterRejection();
    }

    private TokenBucket NewBucket(RateLimit limit) => new(limit.Capacity, limit.RefillPerSecond, _time);

    public RequestGuardVerdict RegisterRejection()
    {
        lock (_lock)
        {
            var now = _time.GetTimestamp();
            _rejections.Enqueue(now);
            while (_rejections.Count > 0
                   && _time.GetElapsedTime(_rejections.Peek(), now) > _limits.RejectionWindow)
                _rejections.Dequeue();

            return _rejections.Count >= _limits.MaxRejectionsPerWindow
                ? RequestGuardVerdict.RejectedTooOften
                : RequestGuardVerdict.Rejected;
        }
    }
}
