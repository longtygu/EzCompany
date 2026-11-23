using System.Text;
using System.Text.Json;
using EzCompany.Core.Abstractions;

namespace EzCompany.Core.Crypto;

/// <summary>
/// Persists DPAPI-protected monotonic timestamp and policy version state in <c>clock.guard</c>
/// to detect offline Windows system clock rollback attacks.
/// </summary>
public sealed class ClockGuardService
{
    private const string DpapiPurpose = "ClockGuardState.v1";
    public static readonly TimeSpan AllowedSkew = TimeSpan.FromMinutes(5);

    private readonly LocalKeyStore _keyStore;
    private readonly DpapiSecretStore _dpapi;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();

    public ClockGuardService(LocalKeyStore keyStore, DpapiSecretStore dpapi, TimeProvider? timeProvider = null)
    {
        _keyStore = keyStore;
        _dpapi = dpapi;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DateTimeOffset GetCurrentUtc() => _timeProvider.GetUtcNow();

    public ClockGuardState? ReadState()
    {
        lock (_gate)
        {
            byte[]? raw = _dpapi.LoadProtectedFile(_keyStore.ClockGuardPath, DpapiPurpose);
            if (raw is null || raw.Length == 0)
            {
                return null;
            }

            return JsonSerializer.Deserialize<ClockGuardState>(Encoding.UTF8.GetString(raw));
        }
    }

    public void RecordVerifiedRun(long policyVersion, DateTimeOffset? overrideUtc = null)
    {
        lock (_gate)
        {
            DateTimeOffset now = overrideUtc ?? _timeProvider.GetUtcNow();
            ClockGuardState? existing = ReadState();

            DateTimeOffset nextTimestamp = existing is not null && existing.LastRunTimestampUtc > now
                ? existing.LastRunTimestampUtc
                : now;

            long nextVersion = existing is not null && existing.LastAcceptedPolicyVersion > policyVersion
                ? existing.LastAcceptedPolicyVersion
                : policyVersion;

            var next = new ClockGuardState(nextTimestamp, nextVersion);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(next));
            _dpapi.SaveProtectedFile(_keyStore.ClockGuardPath, bytes, DpapiPurpose);
        }
    }

    public bool IsClockRollbackDetected(out ClockGuardState? state)
    {
        state = ReadState();
        if (state is null)
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        return now < state.LastRunTimestampUtc - AllowedSkew;
    }
}
