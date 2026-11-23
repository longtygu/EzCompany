using System.Security.Cryptography;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.LanAgent.Database;

public sealed record DatabaseSnapshotArtifact(
    string TempFilePath,
    string Sha256Hex,
    int SchemaVersion,
    long SizeBytes);

/// <summary>
/// Produces a transactionally consistent, non-blocking SQLite snapshot via <c>VACUUM INTO</c>
/// while employees continue writing concurrently in WAL mode.
/// </summary>
public sealed class DatabaseSnapshotService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;

    public DatabaseSnapshotService(IDbContextFactory<AppDbContext> appDbFactory, ILocalStoragePaths paths)
    {
        _appDbFactory = appDbFactory;
        _paths = paths;
    }

    public async Task<DatabaseSnapshotArtifact> CreateConsistentSnapshotAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.TempDirectory);
        string tempPath = Path.Combine(_paths.TempDirectory, $"snapshot_{Guid.NewGuid():N}.db");

        int schemaVersion = GenesisBootstrapService.CurrentAppSchemaVersion;
        await using (AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            var profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (profile is not null)
            {
                schemaVersion = profile.AppSchemaVersion;
            }
        }

        string sourceConnString = $"Data Source={_paths.AppDbPath};Mode=ReadOnly";
        await using (var conn = new SqliteConnection(sourceConnString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $dest;";
            cmd.Parameters.AddWithValue("$dest", tempPath);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            SqliteConnection.ClearPool(conn);
        }

        await using var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        string shaHex = Convert.ToHexString(hash).ToLowerInvariant();
        long size = fs.Length;

        return new DatabaseSnapshotArtifact(tempPath, shaHex, schemaVersion, size);
    }

    public static void SafeDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
