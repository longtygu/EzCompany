using System.Security.Cryptography;
using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Updater;

public sealed record SafeMigrationBackupEntry(
    string FileName,
    string Sha256Hex,
    long SizeBytes);

public sealed record SafeMigrationBackupManifest(
    DateTimeOffset CreatedAtUtc,
    int FromSchemaVersion,
    int ToSchemaVersion,
    IReadOnlyList<SafeMigrationBackupEntry> Files);

public sealed record SafeMigrationResult(
    bool Succeeded,
    bool RolledBack,
    string? BackupDirectory,
    string Message);

/// <summary>
/// Automatically checkpoints WAL, backs up <c>app.db</c> and <c>telemetry.db</c> with SHA-256 verification
/// before applying EF Core migrations, and rolls back byte-for-byte if any migration step throws.
/// </summary>
public sealed class SafeMigrationService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly IDbContextFactory<TelemetryDbContext> _telDbFactory;
    private readonly ILocalStoragePaths _paths;

    public SafeMigrationService(
        IDbContextFactory<AppDbContext> appDbFactory,
        IDbContextFactory<TelemetryDbContext> telDbFactory,
        ILocalStoragePaths paths)
    {
        _appDbFactory = appDbFactory;
        _telDbFactory = telDbFactory;
        _paths = paths;
    }

    public async Task<SafeMigrationResult> MigrateAllSafelyAsync(
        Func<AppDbContext, TelemetryDbContext, Task>? customMigrationAction = null,
        CancellationToken ct = default)
    {
        string? backupDir = null;
        try
        {
            await using AppDbContext appDb = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using TelemetryDbContext telDb = await _telDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            bool hasExistingFiles = File.Exists(_paths.AppDbPath) || File.Exists(_paths.TelemetryDbPath);
            bool hasPendingApp = (await appDb.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).Any();
            bool hasPendingTel = (await telDb.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).Any();

            if (hasExistingFiles && (hasPendingApp || hasPendingTel || customMigrationAction is not null))
            {
                int currentSchema = await TryReadSchemaVersionAsync(appDb, ct).ConfigureAwait(false);
                backupDir = await CreateVerifiedPreMigrationBackupAsync(
                    currentSchema,
                    GenesisBootstrapService.CurrentAppSchemaVersion,
                    ct).ConfigureAwait(false);
            }

            if (customMigrationAction is not null)
            {
                await customMigrationAction(appDb, telDb).ConfigureAwait(false);
            }
            else
            {
                if (hasPendingApp || !File.Exists(_paths.AppDbPath))
                {
                    await appDb.Database.MigrateAsync(ct).ConfigureAwait(false);
                }

                CompanyProfileEntity? company = await appDb.CompanyProfiles.FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (company is not null && company.AppSchemaVersion < GenesisBootstrapService.CurrentAppSchemaVersion)
                {
                    company.AppSchemaVersion = GenesisBootstrapService.CurrentAppSchemaVersion;
                    company.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    await appDb.SaveChangesAsync(ct).ConfigureAwait(false);
                }

                if (hasPendingTel || !File.Exists(_paths.TelemetryDbPath))
                {
                    await telDb.Database.MigrateAsync(ct).ConfigureAwait(false);
                }
            }

            return new SafeMigrationResult(true, false, backupDir, "CSDL đã sẵn sàng và tương thích.");
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(backupDir) && Directory.Exists(backupDir))
            {
                await RestoreFromBackupAsync(backupDir, ct).ConfigureAwait(false);
                return new SafeMigrationResult(
                    false,
                    true,
                    backupDir,
                    $"Migration thất bại ({ex.Message}); đã tự động Rollback nguyên vẹn CSDL từ {backupDir}.");
            }

            return new SafeMigrationResult(false, false, backupDir, $"Migration thất bại: {ex.Message}");
        }
    }

    public IReadOnlyList<string> ListPreMigrationBackups()
    {
        if (!Directory.Exists(_paths.BackupsDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(_paths.BackupsDirectory, "PreMigration_*")
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string> CreateVerifiedPreMigrationBackupAsync(int fromVersion, int toVersion, CancellationToken ct)
    {
        await CheckpointWalIfExistsAsync(_paths.AppDbPath, ct).ConfigureAwait(false);
        await CheckpointWalIfExistsAsync(_paths.TelemetryDbPath, ct).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();

        string dirName = $"PreMigration_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmssfff}_v{fromVersion}_to_v{toVersion}";
        string backupDir = Path.Combine(_paths.BackupsDirectory, dirName);
        Directory.CreateDirectory(backupDir);

        var entries = new List<SafeMigrationBackupEntry>();
        foreach (string sourcePath in new[] { _paths.AppDbPath, _paths.TelemetryDbPath })
        {
            if (!File.Exists(sourcePath)) continue;

            string fileName = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(backupDir, fileName);
            File.Copy(sourcePath, destPath, overwrite: true);

            byte[] bytes = await File.ReadAllBytesAsync(destPath, ct).ConfigureAwait(false);
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            entries.Add(new SafeMigrationBackupEntry(fileName, sha, bytes.Length));
        }

        var manifest = new SafeMigrationBackupManifest(DateTimeOffset.UtcNow, fromVersion, toVersion, entries);
        string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(backupDir, "manifest.json"), manifestJson, ct).ConfigureAwait(false);
        return backupDir;
    }

    private async Task RestoreFromBackupAsync(string backupDir, CancellationToken ct)
    {
        SqliteConnection.ClearAllPools();

        string manifestPath = Path.Combine(backupDir, "manifest.json");
        SafeMigrationBackupManifest manifest = JsonSerializer.Deserialize<SafeMigrationBackupManifest>(
            await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false))
            ?? throw new InvalidOperationException("Manifest sao lưu không hợp lệ.");

        foreach (SafeMigrationBackupEntry entry in manifest.Files)
        {
            string backupFile = Path.Combine(backupDir, entry.FileName);
            byte[] backupBytes = await File.ReadAllBytesAsync(backupFile, ct).ConfigureAwait(false);
            string actualSha = Convert.ToHexString(SHA256.HashData(backupBytes)).ToLowerInvariant();
            if (!string.Equals(actualSha, entry.Sha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException($"Tệp sao lưu {entry.FileName} bị hỏng SHA-256.");
            }

            string targetPath = Path.Combine(_paths.DataDirectory, entry.FileName);
            DeleteWalSidecars(targetPath);
            await File.WriteAllBytesAsync(targetPath, backupBytes, ct).ConfigureAwait(false);
        }
    }

    private static async Task CheckpointWalIfExistsAsync(string dbPath, CancellationToken ct)
    {
        if (!File.Exists(dbPath)) return;
        try
        {
            await using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            SqliteConnection.ClearPool(conn);
        }
        catch
        {
        }
    }

    private static void DeleteWalSidecars(string dbPath)
    {
        foreach (string suffix in new[] { "-wal", "-shm" })
        {
            string sidecar = dbPath + suffix;
            if (File.Exists(sidecar))
            {
                try { File.Delete(sidecar); } catch { }
            }
        }
    }

    private static async Task<int> TryReadSchemaVersionAsync(AppDbContext db, CancellationToken ct)
    {
        try
        {
            CompanyProfileEntity? profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
            return profile?.AppSchemaVersion ?? GenesisBootstrapService.CurrentAppSchemaVersion;
        }
        catch
        {
            return 0;
        }
    }
}
