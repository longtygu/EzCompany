using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EzCompany.Core.LanAgent;

public sealed class DirectorAutoBackupConfig
{
    public bool Enabled { get; set; }
    public int IntervalHours { get; set; } = 24;
    public int RetentionCountPerDevice { get; set; } = 7;
    public DateTimeOffset? LastRunUtc { get; set; }
    public string LastRunSummary { get; set; } = "Chưa chạy sao lưu tự động.";
}

/// <summary>
/// Periodically snapshots online employee SQLite databases (<c>VACUUM INTO</c>) on the Director node
/// with configurable interval and per-device retention pruning.
/// </summary>
public sealed class DirectorAutoBackupService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly DirectorLanClient _lanClient;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly ILogger<DirectorAutoBackupService> _logger;
    private readonly string _configPath;
    private readonly object _configLock = new();
    private DirectorAutoBackupConfig _config;

    public DirectorAutoBackupService(
        IDbContextFactory<AppDbContext> appDbFactory,
        DirectorLanClient lanClient,
        ILocalStoragePaths paths,
        ISessionContext session,
        IAuditLogger audit,
        ILogger<DirectorAutoBackupService> logger)
    {
        _appDbFactory = appDbFactory;
        _lanClient = lanClient;
        _paths = paths;
        _session = session;
        _audit = audit;
        _logger = logger;
        _configPath = Path.Combine(paths.ProfileRoot, "director_autobackup.json");
        _config = LoadConfig();
    }

    public DirectorAutoBackupConfig GetConfig()
    {
        lock (_configLock)
        {
            return new DirectorAutoBackupConfig
            {
                Enabled = _config.Enabled,
                IntervalHours = _config.IntervalHours,
                RetentionCountPerDevice = _config.RetentionCountPerDevice,
                LastRunUtc = _config.LastRunUtc,
                LastRunSummary = _config.LastRunSummary
            };
        }
    }

    public DirectorAutoBackupConfig SaveConfig(bool enabled, int intervalHours, int retentionCountPerDevice)
    {
        lock (_configLock)
        {
            _config.Enabled = enabled;
            _config.IntervalHours = Math.Clamp(intervalHours, 1, 168);
            _config.RetentionCountPerDevice = Math.Clamp(retentionCountPerDevice, 1, 50);
            PersistConfigLocked();
            return GetConfig();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!_session.IsAuthenticated || !_session.IsDirectorNode)
                {
                    continue;
                }

                DirectorAutoBackupConfig cfg = GetConfig();
                if (!cfg.Enabled)
                {
                    continue;
                }

                bool due = !cfg.LastRunUtc.HasValue ||
                           (DateTimeOffset.UtcNow - cfg.LastRunUtc.Value) >= TimeSpan.FromHours(cfg.IntervalHours);
                if (due)
                {
                    await RunBackupAllOnlineNowAsync(stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task<string> RunBackupAllOnlineNowAsync(CancellationToken ct = default)
    {
        DirectorAutoBackupConfig cfg = GetConfig();
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<PairedDeviceEntity> targets = await db.PairedDevices
            .Where(d => d.MachineId != _paths.MachineId &&
                        d.IsOnline &&
                        !string.IsNullOrWhiteSpace(d.LastKnownIp) &&
                        d.LastKnownPort > 0)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int succeeded = 0;
        int failed = 0;
        string rootDir = Path.Combine(_paths.DirectorCacheDirectory, "AutoBackups");

        foreach (PairedDeviceEntity dev in targets)
        {
            try
            {
                string endpoint = $"{dev.LastKnownIp}:{dev.LastKnownPort}";
                DownloadedArtifact snap = await _lanClient.DownloadDatabaseSnapshotAsync(endpoint, ct).ConfigureAwait(false);
                string devDir = Path.Combine(rootDir, SanitizeSegment(dev.MachineId));
                Directory.CreateDirectory(devDir);
                string destPath = Path.Combine(devDir, $"{SanitizeSegment(dev.MachineId)}_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.db");
                await File.WriteAllBytesAsync(destPath, snap.Bytes, ct).ConfigureAwait(false);

                _ = PruneBackupFiles(devDir, cfg.RetentionCountPerDevice);
                dev.LastBackupUtc = DateTimeOffset.UtcNow;
                succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Auto backup failed for {MachineId}.", dev.MachineId);
                failed++;
            }
        }

        if (succeeded > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        string summary = $"Lúc {DateTimeOffset.Now:HH:mm dd/MM/yyyy}: Thành công {succeeded} máy, Lỗi {failed} máy (giữ tối đa {cfg.RetentionCountPerDevice} bản/máy).";
        lock (_configLock)
        {
            _config.LastRunUtc = DateTimeOffset.UtcNow;
            _config.LastRunSummary = summary;
            PersistConfigLocked();
        }

        await _audit.LogAsync("DIRECTOR_AUTO_BACKUP", "core.director", _paths.MachineId, summary, failed > 0 ? "Warning" : "Info", ct)
            .ConfigureAwait(false);
        return summary;
    }

    public static int PruneBackupFiles(string deviceBackupDir, int retentionCount)
    {
        if (!Directory.Exists(deviceBackupDir) || retentionCount < 1)
        {
            return 0;
        }

        FileInfo[] files = new DirectoryInfo(deviceBackupDir)
            .GetFiles("*.db", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(f => f.LastWriteTimeUtc)
            .ToArray();

        int deleted = 0;
        for (int i = retentionCount; i < files.Length; i++)
        {
            try
            {
                files[i].Delete();
                deleted++;
            }
            catch
            {
            }
        }

        return deleted;
    }

    private DirectorAutoBackupConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                DirectorAutoBackupConfig? parsed = JsonSerializer.Deserialize<DirectorAutoBackupConfig>(json, JsonOptions);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }
        catch
        {
        }

        return new DirectorAutoBackupConfig();
    }

    private void PersistConfigLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, JsonOptions));
        }
        catch
        {
        }
    }

    private static string SanitizeSegment(string s)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
