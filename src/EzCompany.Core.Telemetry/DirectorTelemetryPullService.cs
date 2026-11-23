using System.Security.Cryptography;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent;
using EzCompany.Core.LanAgent.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EzCompany.Core.Telemetry;

/// <summary>
/// Director-side pull service that syncs delta telemetry (presence, app usage, encrypted 320px thumbnails)
/// into the Director's local cache (<c>telemetry.db</c>) and fetches/decrypts 1280px images on demand.
/// </summary>
public sealed class DirectorTelemetryPullService : BackgroundService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly IDbContextFactory<TelemetryDbContext> _telDbFactory;
    private readonly LocalKeyStore _keyStore;
    private readonly EciesCryptoService _ecies;
    private readonly DirectorLanClient _lanClient;
    private readonly IEventBus _eventBus;
    private readonly ILocalStoragePaths _paths;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DirectorTelemetryPullService> _logger;

    public DirectorTelemetryPullService(
        IDbContextFactory<AppDbContext> appDbFactory,
        IDbContextFactory<TelemetryDbContext> telDbFactory,
        LocalKeyStore keyStore,
        EciesCryptoService ecies,
        DirectorLanClient lanClient,
        IEventBus eventBus,
        ILocalStoragePaths paths,
        ILogger<DirectorTelemetryPullService> logger,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _telDbFactory = telDbFactory;
        _keyStore = keyStore;
        _ecies = ecies;
        _lanClient = lanClient;
        _eventBus = eventBus;
        _paths = paths;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await PullAllOnlinePeersAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task PullAllOnlinePeersAsync(CancellationToken ct = default)
    {
        if (!_keyStore.HasRootPrivateKey)
        {
            return;
        }

        await using AppDbContext appDb = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<PairedDeviceEntity> onlinePeers = await appDb.PairedDevices
            .Where(x => x.IsOnline &&
                        x.MachineId != _paths.MachineId &&
                        x.LastKnownIp != null &&
                        x.LastKnownPort > 0)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (PairedDeviceEntity peer in onlinePeers)
        {
            try
            {
                await PullFromPeerAsync(peer.MachineId, $"{peer.LastKnownIp}:{peer.LastKnownPort}", ct)
                    .ConfigureAwait(false);

                peer.LastTelemetrySyncUtc = _timeProvider.GetUtcNow();
                await appDb.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Telemetry delta pull failed for {MachineId}.", peer.MachineId);
            }
        }
    }

    public async Task PullFromPeerAsync(string peerMachineId, string endpoint, CancellationToken ct = default)
    {
        await using TelemetryDbContext telDb = await _telDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        SyncCheckpointEntity? checkpoint = await telDb.SyncCheckpoints
            .FirstOrDefaultAsync(x => x.PeerMachineId == peerMachineId, ct)
            .ConfigureAwait(false);

        DateTimeOffset since = checkpoint?.LastTelemetryPulledUtc ?? DateTimeOffset.MinValue;
        TelemetryDeltaResponseDto delta = await _lanClient.PullTelemetryDeltaAsync(endpoint, since, ct)
            .ConfigureAwait(false);

        foreach (TelemetryPresenceDto p in delta.PresenceSessions)
        {
            PresenceSessionEntity? existing = await telDb.PresenceSessions
                .FirstOrDefaultAsync(x => x.Id == p.Id, ct)
                .ConfigureAwait(false);

            if (existing is null)
            {
                telDb.PresenceSessions.Add(new PresenceSessionEntity
                {
                    Id = p.Id,
                    MachineId = p.MachineId,
                    UserId = p.UserId,
                    Username = p.Username,
                    DateKey = p.DateKey,
                    StartedAtUtc = p.StartedAtUtc,
                    EndedAtUtc = p.EndedAtUtc,
                    State = p.State,
                    DurationSeconds = p.DurationSeconds,
                    UpdatedAtUtc = p.UpdatedAtUtc
                });
            }
            else
            {
                existing.EndedAtUtc = p.EndedAtUtc;
                existing.DurationSeconds = p.DurationSeconds;
                existing.UpdatedAtUtc = p.UpdatedAtUtc;
            }
        }

        foreach (TelemetryAppUsageDto u in delta.AppUsages)
        {
            AppUsageRecordEntity? existing = await telDb.AppUsageRecords
                .FirstOrDefaultAsync(x => x.Id == u.Id, ct)
                .ConfigureAwait(false);

            if (existing is null)
            {
                telDb.AppUsageRecords.Add(new AppUsageRecordEntity
                {
                    Id = u.Id,
                    MachineId = u.MachineId,
                    UserId = u.UserId,
                    Username = u.Username,
                    DateKey = u.DateKey,
                    ProcessName = u.ProcessName,
                    SanitizedWindowTitle = u.SanitizedWindowTitle,
                    ActiveSeconds = u.ActiveSeconds,
                    LastSeenUtc = u.LastSeenUtc
                });
            }
            else
            {
                existing.ActiveSeconds = u.ActiveSeconds;
                existing.LastSeenUtc = u.LastSeenUtc;
            }
        }

        foreach (TelemetrySnapshotMetaDto s in delta.Snapshots)
        {
            bool exists = await telDb.EncryptedSnapshots.AnyAsync(x => x.Id == s.Id, ct).ConfigureAwait(false);
            if (!exists)
            {
                telDb.EncryptedSnapshots.Add(new EncryptedSnapshotEntity
                {
                    Id = s.Id,
                    MachineId = s.MachineId,
                    UserId = s.UserId,
                    Username = s.Username,
                    CapturedAtUtc = s.CapturedAtUtc,
                    Width = s.Width,
                    Height = s.Height,
                    ThumbnailEncryptedBlob = s.ThumbnailEncryptedBlob,
                    FullImageEncryptedBlob = [],
                    ThumbnailSizeBytes = s.ThumbnailSizeBytes,
                    FullImageSizeBytes = s.FullImageSizeBytes,
                    IsFullDownloadedOnDirector = false
                });
            }
        }

        DateTimeOffset maxUpdated = delta.PresenceSessions.Select(x => x.UpdatedAtUtc)
            .Concat(delta.AppUsages.Select(x => x.LastSeenUtc))
            .Concat(delta.Snapshots.Select(x => x.CapturedAtUtc))
            .DefaultIfEmpty(since)
            .Max();

        if (checkpoint is null)
        {
            telDb.SyncCheckpoints.Add(new SyncCheckpointEntity
            {
                Id = Guid.NewGuid(),
                PeerMachineId = peerMachineId,
                LastTelemetryPulledUtc = maxUpdated,
                LastSnapshotPulledUtc = maxUpdated,
                UpdatedAtUtc = _timeProvider.GetUtcNow()
            });
        }
        else
        {
            checkpoint.LastTelemetryPulledUtc = maxUpdated;
            checkpoint.LastSnapshotPulledUtc = maxUpdated;
            checkpoint.UpdatedAtUtc = _timeProvider.GetUtcNow();
        }

        await telDb.SaveChangesAsync(ct).ConfigureAwait(false);

        if (delta.PresenceSessions.Count > 0 || delta.AppUsages.Count > 0 || delta.Snapshots.Count > 0)
        {
            _eventBus.Publish(new TelemetrySyncCompletedEvent(
                peerMachineId,
                delta.PresenceSessions.Count,
                delta.AppUsages.Count,
                delta.Snapshots.Count,
                _timeProvider.GetUtcNow()));
        }
    }

    public byte[] DecryptBlobWithDirectorKey(byte[] encryptedBlob)
    {
        byte[] rootPriv = _keyStore.LoadRootPrivateKey()
            ?? throw new InvalidOperationException("Chỉ máy Giám đốc có RootPrivateKey mới có quyền giải mã ảnh Telemetry.");
        byte[] encPriv = _ecies.DeriveCompanyX25519PrivateKey(rootPriv);
        try
        {
            return _ecies.Decrypt(encPriv, encryptedBlob);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootPriv);
            CryptographicOperations.ZeroMemory(encPriv);
        }
    }

    public async Task<byte[]> GetOrFetchDecryptedFullSnapshotAsync(
        Guid snapshotId,
        string? endpointOverride = null,
        CancellationToken ct = default)
    {
        await using TelemetryDbContext telDb = await _telDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        EncryptedSnapshotEntity snap = await telDb.EncryptedSnapshots
            .FirstOrDefaultAsync(x => x.Id == snapshotId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy bản ghi ảnh chụp.");

        if (snap.FullImageEncryptedBlob.Length > 0)
        {
            return DecryptBlobWithDirectorKey(snap.FullImageEncryptedBlob);
        }

        string endpoint = endpointOverride ?? string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            await using AppDbContext appDb = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            PairedDeviceEntity? peer = await appDb.PairedDevices.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MachineId == snap.MachineId, ct)
                .ConfigureAwait(false);

            if (peer is null || !peer.IsOnline || string.IsNullOrWhiteSpace(peer.LastKnownIp) || peer.LastKnownPort <= 0)
            {
                throw new InvalidOperationException("Máy nhân viên đang ngoại tuyến nên chưa thể tải ảnh rõ nét 1280px.");
            }

            endpoint = $"{peer.LastKnownIp}:{peer.LastKnownPort}";
        }

        TelemetryFullSnapshotDto fullDto = await _lanClient
            .FetchFullSnapshotAsync(endpoint, snapshotId, ct)
            .ConfigureAwait(false);

        snap.FullImageEncryptedBlob = fullDto.FullImageEncryptedBlob;
        snap.FullImageSizeBytes = fullDto.FullImageEncryptedBlob.Length;
        snap.IsFullDownloadedOnDirector = true;
        await telDb.SaveChangesAsync(ct).ConfigureAwait(false);

        return DecryptBlobWithDirectorKey(fullDto.FullImageEncryptedBlob);
    }
}
