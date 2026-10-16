using System.Collections.Concurrent;

namespace Madlands.Server.Net;

/// <summary>
/// Thread-safe token buckets per key (IP address, account name). A rate of 0 disables limiting.
/// Buckets are dropped wholesale when the table grows large; worst case a client gets a fresh bucket.
/// </summary>
public sealed class KeyedRateLimiter
{
    private const int MaxKeys = 50_000;

    private readonly ConcurrentDictionary<string, RateLimiter> _buckets = new();
    private readonly float _perSecond;
    private readonly float _burst;

    public KeyedRateLimiter(int perMinute, int burst)
    {
        _perSecond = perMinute / 60f;
        _burst = burst;
    }

    public bool Enabled => _perSecond > 0;

    /// <summary>Consumes one token; false if the key is over its rate.</summary>
    public bool TryConsume(string key)
    {
        if (!Enabled) return true;
        var bucket = GetBucket(key);
        lock (bucket) return bucket.TryConsume(Environment.TickCount64);
    }

    /// <summary>True if the key has at least one token left, without consuming it.</summary>
    public bool CanProceed(string key)
    {
        if (!Enabled) return true;
        var bucket = GetBucket(key);
        lock (bucket) return bucket.Peek(Environment.TickCount64);
    }

    private RateLimiter GetBucket(string key)
    {
        if (_buckets.Count > MaxKeys) _buckets.Clear();
        return _buckets.GetOrAdd(key, _ => new RateLimiter(_perSecond, _burst));
    }
}
