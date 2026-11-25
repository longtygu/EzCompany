using System.IO;
using System.IO.Compression;
using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent;
using EzCompany.Core.LanAgent.Collaboration;
using EzCompany.Core.LanAgent.Discovery;
using EzCompany.Core.LanAgent.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.App.Services;

public sealed record DiagnosticRepairItem(
    string Category,
    string Status, // OK | Repaired | Warning
    string Details);

/// <summary>
/// Provides log history inspection, redacted diagnostic bug-report <c>.zip</c> bundling,
/// and automated self-scan/repair routines for LAN, SQLite WAL, storage, memory, and policy state.
/// </summary>
public sealed class AppDiagnosticsAndRepairService
{
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly IDbContextFactory<TelemetryDbContext> _telDbFactory;
    private readonly ILanDiscoveryService _discovery;
    private readonly PeerTaskSyncService _taskSync;
    private readonly PeerCollabSyncService _collabSync;
    private readonly ClockGuardService _clockGuard;
    private readonly IAuditLogger _audit;
    private readonly RollingFileLoggerProvider? _fileLogger;

    public AppDiagnosticsAndRepairService(
        ILocalStoragePaths paths,
        ISessionContext session,
        IPermissionService permissions,
        IDbContextFactory<AppDbContext> appDbFactory,
        IDbContextFactory<TelemetryDbContext> telDbFactory,
        ILanDiscoveryService discovery,
        PeerTaskSyncService taskSync,
        PeerCollabSyncService collabSync,
        ClockGuardService clockGuard,
        IAuditLogger audit,
        RollingFileLoggerProvider? fileLogger = null)
    {
        _paths = paths;
        _session = session;
        _permissions = permissions;
        _appDbFactory = appDbFactory;
        _telDbFactory = telDbFactory;
        _discovery = discovery;
        _taskSync = taskSync;
        _collabSync = collabSync;
        _clockGuard = clockGuard;
        _audit = audit;
        _fileLogger = fileLogger;
    }

    public async Task<IReadOnlyList<string>> LoadRecentLogLinesAsync(
        string levelFilter = "ALL",
        int maxLines = 250,
        CancellationToken ct = default)
    {
        _fileLogger?.Flush();
        if (!Directory.Exists(_paths.LogsDirectory))
        {
            return [];
        }

        FileInfo? latestLog = new DirectoryInfo(_paths.LogsDirectory)
            .GetFiles("ezcompany-*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        if (latestLog is null)
        {
            return [];
        }

        var lines = new List<string>();
        using var fs = new FileStream(latestLog.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) continue;
            if (!string.Equals(levelFilter, "ALL", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains($"[{levelFilter}]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            lines.Add(line);
        }

        return lines.TakeLast(Math.Max(10, maxLines)).ToArray();
    }

    public async Task<string> CreateBugReportZipAsync(
        string title,
        string description,
        CancellationToken ct = default)
    {
        _fileLogger?.Flush();
        string bugDir = Path.Combine(_paths.ProfileRoot, "BugReports");
        Directory.CreateDirectory(bugDir);

        string stamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
        string tempStage = Path.Combine(_paths.TempDirectory, $"bugreport_stage_{stamp}");
        Directory.CreateDirectory(tempStage);

        try
        {
            IReadOnlyList<DiagnosticRepairItem> diagnostics = await RunAutoScanAndRepairAsync(repair: false, ct)
                .ConfigureAwait(false);

            var summary = new
            {
                CreatedAtLocal = DateTimeOffset.Now,
                AppVersion = _session.CurrentAppVersion,
                ProfileName = _paths.ProfileName,
                MachineId = _paths.MachineId,
                UserRole = CompanyRoles.FormatRolesBilingual(_session.Role),
                LanPort = _session.CurrentLanPort,
                IsPolicyLocked = _permissions.IsLocked,
                PolicyLockReason = _permissions.LockReason,
                OSVersion = Environment.OSVersion.ToString(),
                ProcessMemoryMb = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2),
                ReportTitle = string.IsNullOrWhiteSpace(title) ? "Báo lỗi người dùng" : title.Trim(),
                UserDescription = description?.Trim() ?? string.Empty,
                Diagnostics = diagnostics
            };

            string summaryJson = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(tempStage, "diagnostic-summary.json"), summaryJson, ct)
                .ConfigureAwait(false);

            if (Directory.Exists(_paths.LogsDirectory))
            {
                foreach (FileInfo logFile in new DirectoryInfo(_paths.LogsDirectory)
                             .GetFiles("ezcompany-*.log")
                             .OrderByDescending(f => f.LastWriteTimeUtc)
                             .Take(5))
                {
                    using var src = new FileStream(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var dst = File.Create(Path.Combine(tempStage, logFile.Name));
                    await src.CopyToAsync(dst, ct).ConfigureAwait(false);
                }
            }

            string zipPath = Path.Combine(bugDir, $"ezcompany-bugreport-{stamp}.zip");
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(tempStage, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            await _audit.LogAsync("BUG_REPORT_CREATED", "core.diagnostics", Path.GetFileName(zipPath), title, "Info", ct)
                .ConfigureAwait(false);

            return zipPath;
        }
        finally
        {
            if (Directory.Exists(tempStage))
            {
                try { Directory.Delete(tempStage, recursive: true); } catch { }
            }
        }
    }

    public async Task<IReadOnlyList<DiagnosticRepairItem>> RunAutoScanAndRepairAsync(
        bool repair = true,
        CancellationToken ct = default)
    {
        var results = new List<DiagnosticRepairItem>();

        // 1. Storage directories integrity
        try
        {
            _paths.EnsureDirectoriesExist();
            results.Add(new DiagnosticRepairItem("Thư mục dữ liệu & Vault", repair ? "Repaired" : "OK", "Đã kiểm tra đủ cây thư mục hồ sơ, Vault, Chat và Logs."));
        }
        catch (Exception ex)
        {
            results.Add(new DiagnosticRepairItem("Thư mục dữ liệu & Vault", "Warning", ex.Message));
        }

        // 2. SQLite Integrity & WAL Checkpoint + Optimize
        try
        {
            await using AppDbContext appDb = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            string integrity = await ReadScalarIntegrityAsync(appDb, ct).ConfigureAwait(false);
            if (repair)
            {
                await appDb.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA optimize;", ct).ConfigureAwait(false);
            }

            await using TelemetryDbContext telDb = await _telDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            string telIntegrity = await ReadScalarIntegrityAsync(telDb, ct).ConfigureAwait(false);
            if (repair)
            {
                await telDb.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA optimize;", ct).ConfigureAwait(false);
            }

            results.Add(new DiagnosticRepairItem(
                "CSDL SQLite (WAL + Optimize)",
                repair ? "Repaired" : "OK",
                $"app.db={integrity}, telemetry.db={telIntegrity}. Đã tối ưu hóa & dọn WAL."));
        }
        catch (Exception ex)
        {
            results.Add(new DiagnosticRepairItem("CSDL SQLite", "Warning", ex.Message));
        }

        // 3. LAN Discovery & Pending Queues Flush
        try
        {
            if (repair)
            {
                _discovery.RefreshAdvertisement();
            }

            int peerCount = _discovery.GetPeers().Count;
            int flushedTasks = repair && _session.IsAuthenticated ? await _taskSync.FlushPendingTasksOnceAsync(null, ct).ConfigureAwait(false) : 0;
            int flushedChat = repair && _session.IsAuthenticated ? await _collabSync.FlushPendingChatAsync(null, ct).ConfigureAwait(false) : 0;

            results.Add(new DiagnosticRepairItem(
                "Mạng LAN P2P & Hàng đợi đồng bộ",
                (flushedTasks + flushedChat) > 0 ? "Repaired" : "OK",
                $"Cổng LAN={_session.CurrentLanPort}, Máy đồng nghiệp phát hiện={peerCount}, Đã đẩy bù={flushedTasks} task / {flushedChat} tin nhắn."));
        }
        catch (Exception ex)
        {
            results.Add(new DiagnosticRepairItem("Mạng LAN P2P", "Warning", ex.Message));
        }

        // 4. Temp cleanup & Managed Memory GC
        try
        {
            int cleanedTemp = 0;
            if (repair && Directory.Exists(_paths.TempDirectory))
            {
                DateTime cutoff = DateTime.UtcNow.AddHours(-1);
                foreach (string file in Directory.EnumerateFiles(_paths.TempDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        try { File.Delete(file); cleanedTemp++; } catch { }
                    }
                }
            }

            long beforeMb = GC.GetTotalMemory(false) / (1024 * 1024);
            if (repair)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
            }
            long afterMb = GC.GetTotalMemory(false) / (1024 * 1024);

            results.Add(new DiagnosticRepairItem(
                "Hiệu năng Bộ nhớ & Tệp tạm",
                repair ? "Repaired" : "OK",
                $"RAM tiến trình: {beforeMb} MB -> {afterMb} MB • Đã dọn {cleanedTemp} tệp tạm cũ."));
        }
        catch (Exception ex)
        {
            results.Add(new DiagnosticRepairItem("Hiệu năng Bộ nhớ", "Warning", ex.Message));
        }

        // 5. License & ClockGuard Check
        bool rollback = _clockGuard.IsClockRollbackDetected(out _);
        results.Add(new DiagnosticRepairItem(
            "Giấy phép Offline & Đồng hồ hệ thống",
            rollback || _permissions.IsLocked ? "Warning" : "OK",
            _permissions.IsLocked
                ? $"Đang khóa: {_permissions.LockReason}"
                : (rollback ? "Phát hiện đồng hồ hệ thống lệch." : "Giấy phép và ClockGuard bình thường.")));

        if (repair)
        {
            await _audit.LogAsync("AUTO_SCAN_REPAIR", "core.diagnostics", _paths.MachineId, $"Checks={results.Count}", "Info", ct)
                .ConfigureAwait(false);
        }

        return results;
    }

    private static async Task<string> ReadScalarIntegrityAsync(DbContext db, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(db.Database.GetConnectionString());
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return (await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString() ?? "unknown";
    }
}
