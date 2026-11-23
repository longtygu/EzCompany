using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace EzCompany.Core.Telemetry;

/// <summary>
/// Tracks Active/Idle presence (<c>GetLastInputInfo</c> + <c>SessionSwitch</c>, 180s idle threshold)
/// and sanitized foreground application usage every 15 seconds into <c>telemetry.db</c>.
/// </summary>
public sealed class TelemetryCollectorService : BackgroundService
{
    public const int SampleIntervalSeconds = 15;
    public const int IdleThresholdSeconds = 180;

    private readonly IDbContextFactory<TelemetryDbContext> _telDbFactory;
    private readonly ISessionContext _session;
    private readonly ILocalStoragePaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TelemetryCollectorService> _logger;

    private volatile bool _isSessionLocked;
    private Guid? _currentSessionId;
    private string? _currentState;
    private string? _currentDateKey;

    public TelemetryCollectorService(
        IDbContextFactory<TelemetryDbContext> telDbFactory,
        ISessionContext session,
        ILocalStoragePaths paths,
        ILogger<TelemetryCollectorService> logger,
        TimeProvider? timeProvider = null)
    {
        _telDbFactory = telDbFactory;
        _session = session;
        _paths = paths;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SystemEvents.SessionSwitch subscription unavailable.");
        }

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(SampleIntervalSeconds));
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!_session.IsAuthenticated)
                {
                    continue;
                }

                double idleSec = Win32ActivityInterop.GetIdleSeconds();
                bool isIdle = _isSessionLocked || idleSec >= IdleThresholdSeconds;
                string? proc = null;
                string? title = null;
                if (!isIdle)
                {
                    Win32ActivityInterop.TryGetForegroundWindowInfo(out proc, out title);
                }

                await RecordSampleAsync(isIdle, proc, title, SampleIntervalSeconds, stoppingToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
            }
            catch
            {
            }
        }
    }

    public async Task RecordSampleAsync(
        bool isIdle,
        string? foregroundProcessName,
        string? foregroundWindowTitle,
        long elapsedSeconds = SampleIntervalSeconds,
        CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated || _session.UserId == Guid.Empty)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string dateKey = now.ToLocalTime().ToString("yyyy-MM-dd");
        string state = isIdle ? "Idle" : "Active";

        await using TelemetryDbContext db = await _telDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        if (_currentSessionId.HasValue &&
            string.Equals(_currentState, state, StringComparison.Ordinal) &&
            string.Equals(_currentDateKey, dateKey, StringComparison.Ordinal))
        {
            PresenceSessionEntity? existingSession = await db.PresenceSessions
                .FirstOrDefaultAsync(x => x.Id == _currentSessionId.Value, ct)
                .ConfigureAwait(false);

            if (existingSession is not null)
            {
                existingSession.EndedAtUtc = now;
                existingSession.DurationSeconds += elapsedSeconds;
                existingSession.UpdatedAtUtc = now;
            }
            else
            {
                _currentSessionId = null;
            }
        }
        else
        {
            _currentSessionId = null;
        }

        if (_currentSessionId is null)
        {
            Guid newId = Guid.NewGuid();
            db.PresenceSessions.Add(new PresenceSessionEntity
            {
                Id = newId,
                MachineId = _paths.MachineId,
                UserId = _session.UserId,
                Username = _session.Username,
                DateKey = dateKey,
                StartedAtUtc = now,
                EndedAtUtc = now,
                State = state,
                DurationSeconds = elapsedSeconds,
                UpdatedAtUtc = now
            });
            _currentSessionId = newId;
            _currentState = state;
            _currentDateKey = dateKey;
        }

        if (!isIdle && !string.IsNullOrWhiteSpace(foregroundProcessName))
        {
            string proc = foregroundProcessName.Trim();
            string sanitized = PrivacySanitizer.SanitizeWindowTitle(proc, foregroundWindowTitle);

            AppUsageRecordEntity? usage = await db.AppUsageRecords
                .FirstOrDefaultAsync(
                    x => x.MachineId == _paths.MachineId &&
                         x.DateKey == dateKey &&
                         x.ProcessName == proc &&
                         x.SanitizedWindowTitle == sanitized,
                    ct)
                .ConfigureAwait(false);

            if (usage is null)
            {
                db.AppUsageRecords.Add(new AppUsageRecordEntity
                {
                    Id = Guid.NewGuid(),
                    MachineId = _paths.MachineId,
                    UserId = _session.UserId,
                    Username = _session.Username,
                    DateKey = dateKey,
                    ProcessName = proc,
                    SanitizedWindowTitle = sanitized,
                    ActiveSeconds = elapsedSeconds,
                    LastSeenUtc = now
                });
            }
            else
            {
                usage.ActiveSeconds += elapsedSeconds;
                usage.LastSeenUtc = now;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect)
        {
            _isSessionLocked = true;
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect)
        {
            _isSessionLocked = false;
        }
    }
}
