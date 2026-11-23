using System.Collections.Concurrent;

namespace EzCompany.Core.LanAgent.Security;

/// <summary>
/// Thread-safe sliding window cache preventing replay of <c>X-Ez-Nonce</c> values within 2 minutes.
/// </summary>
public sealed class NonceReplayCache
{
    public static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private long _lastCleanupTicks;

    public bool TryAccept(string nonce, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length < 8 || nonce.Length > 128)
        {
            return false;
        }

        CleanupExpiredIfNeeded(nowUtc);

        DateTimeOffset expiresAt = nowUtc.Add(ReplayWindow);
        if (_seen.TryAdd(nonce, expiresAt))
        {
            return true;
        }

        // If previously seen entry has already expired, replace atomically
        if (_seen.TryGetValue(nonce, out DateTimeOffset existingExpiry) && existingExpiry <= nowUtc)
        {
            return _seen.TryUpdate(nonce, expiresAt, existingExpiry);
        }

        return false;
    }

    private void CleanupExpiredIfNeeded(DateTimeOffset nowUtc)
    {
        long nowTicks = nowUtc.UtcTicks;
        long prevTicks = Interlocked.Read(ref _lastCleanupTicks);
        if (nowTicks - prevTicks < TimeSpan.FromSeconds(30).Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastCleanupTicks, nowTicks, prevTicks) != prevTicks)
        {
            return;
        }

        foreach ((string key, DateTimeOffset exp) in _seen)
        {
            if (exp <= nowUtc)
            {
                _seen.TryRemove(key, out _);
            }
        }
    }
}
