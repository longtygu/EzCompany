using System.IO;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EzCompany.Core.Telemetry;

/// <summary>
/// FIFO Ring Buffer retention manager for <c>telemetry.db</c>:
/// deletes records older than 7 days and keeps DB size under 200 MB.
/// </summary>
public sealed class TelemetryRetentionService : BackgroundService
{
    public const int DefaultRetentionDays = 7;
    public const long DefaultMaxBytes = 200L * 1024 * 1024;

    private readonly IDbContextFactory<TelemetryDbContext> _telDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TelemetryRetentionService> _logger;

    public TelemetryRetentionService(
        IDbContextFactory<TelemetryDbContext> telDbFactory,
        ILocalStoragePaths paths,
        ILogger<TelemetryRetentionService> logger,
        TimeProvider? timeProvider = null)
    {
        _telDbFactory = telDbFactory;
        _paths = paths;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        try
        {
            await RunCleanupAsync(DefaultRetentionDays, DefaultMaxBytes, stoppingToken).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunCleanupAsync(DefaultRetentionDays, DefaultMaxBytes, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task<int> RunCleanupAsync(
        int retentionDays = DefaultRetentionDays,
        long maxBytes = DefaultMaxBytes,
        CancellationToken ct = default)
    {
        int removed = 0;
        DateTimeOffset cutoff = _timeProvider.GetUtcNow().AddDays(-retentionDays);

        await using TelemetryDbContext db = await _telDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        List<EncryptedSnapshotEntity> expiredSnaps = await db.EncryptedSnapshots
            .Where(x => x.CapturedAtUtc < cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (expiredSnaps.Count > 0)
        {
            db.EncryptedSnapshots.RemoveRange(expiredSnaps);
            removed += expiredSnaps.Count;
        }

        List<AppUsageRecordEntity> expiredUsage = await db.AppUsageRecords
            .Where(x => x.LastSeenUtc < cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (expiredUsage.Count > 0)
        {
            db.AppUsageRecords.RemoveRange(expiredUsage);
            removed += expiredUsage.Count;
        }

        List<PresenceSessionEntity> expiredPresence = await db.PresenceSessions
            .Where(x => x.UpdatedAtUtc < cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (expiredPresence.Count > 0)
        {
            db.PresenceSessions.RemoveRange(expiredPresence);
            removed += expiredPresence.Count;
        }

        if (removed > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Enforce FIFO byte cap (active DB/blob footprint)
        long currentBytes = EstimateTelemetryFootprintBytes(db);
        while (currentBytes > maxBytes)
        {
            EncryptedSnapshotEntity? oldestSnap = await db.EncryptedSnapshots
                .OrderBy(x => x.CapturedAtUtc)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (oldestSnap is not null)
            {
                db.EncryptedSnapshots.Remove(oldestSnap);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                removed++;
                currentBytes = EstimateTelemetryFootprintBytes(db);
                continue;
            }

            List<AppUsageRecordEntity> oldestUsage = await db.AppUsageRecords
                .OrderBy(x => x.LastSeenUtc)
                .Take(50)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (oldestUsage.Count == 0)
            {
                break;
            }

            db.AppUsageRecords.RemoveRange(oldestUsage);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            removed += oldestUsage.Count;
            currentBytes = EstimateTelemetryFootprintBytes(db);
        }

        if (removed > 0)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("VACUUM;", ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Telemetry VACUUM skipped.");
            }

            _logger.LogInformation("Telemetry FIFO Ring Buffer pruned {Count} records.", removed);
        }

        return removed;
    }

    private long EstimateTelemetryFootprintBytes(TelemetryDbContext db)
    {
        long blobBytes = db.EncryptedSnapshots.AsNoTracking()
            .Select(x => (long)x.ThumbnailSizeBytes + x.FullImageSizeBytes)
            .AsEnumerable()
            .Sum();
        long rowEstimate = (db.PresenceSessions.Count() + db.AppUsageRecords.Count()) * 256L;
        return blobBytes + rowEstimate;
    }
}
