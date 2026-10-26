namespace Server.Application.Coordinator.Connections.RateLimiting;

public sealed class TokenBucket
{
    private readonly object _lock = new();
    private readonly TimeProvider _time;
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private double _tokens;
    private long _lastRefill;

    public TokenBucket(int capacity, double refillPerSecond, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillPerSecond);

        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _time = time;
        _tokens = capacity;
        _lastRefill = time.GetTimestamp();
    }

    public bool IsFull
    {
        get
        {
            lock (_lock)
            {
                Refill();
                return _tokens >= _capacity;
            }
        }
    }

    public bool TryTake()
    {
        lock (_lock)
        {
            Refill();
            if (_tokens < 1)
                return false;

            _tokens -= 1;
            return true;
        }
    }

    private void Refill()
    {
        var now = _time.GetTimestamp();
        var elapsed = _time.GetElapsedTime(_lastRefill, now).TotalSeconds;
        _lastRefill = now;
        _tokens = Math.Min(_capacity, _tokens + elapsed * _refillPerSecond);
    }
}
