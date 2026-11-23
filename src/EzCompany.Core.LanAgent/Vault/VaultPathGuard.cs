using System.Security.Cryptography;
using EzCompany.Core.Abstractions;
using EzCompany.Core.LanAgent.Models;

namespace EzCompany.Core.LanAgent.Vault;

/// <summary>
/// Strictly resolves paths inside <see cref="ILocalStoragePaths.VaultDirectory"/>
/// and blocks path traversal, absolute paths, drive-relative escapes, null bytes, and symlinks.
/// </summary>
public sealed class VaultPathGuard
{
    private readonly string _vaultRoot;

    public VaultPathGuard(ILocalStoragePaths paths)
    {
        _vaultRoot = Path.GetFullPath(paths.VaultDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public bool TryResolveSafeFile(string? relativePath, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "Đường dẫn tệp không được để trống.";
            return false;
        }

        if (relativePath.IndexOf('\0') >= 0 || relativePath.Contains(':', StringComparison.Ordinal))
        {
            error = "Đường dẫn chứa ký tự bị cấm.";
            return false;
        }

        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart();

        if (Path.IsPathRooted(normalized))
        {
            error = "Không cho phép đường dẫn tuyệt đối.";
            return false;
        }

        string[] segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
        {
            error = "Phát hiện Path Traversal (../).";
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(_vaultRoot, Path.Combine(segments)));
        string expectedPrefix = _vaultRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Đường dẫn vượt ra ngoài thư mục Vault.";
            return false;
        }

        if (File.Exists(candidate))
        {
            FileInfo fi = new(candidate);
            if (fi.LinkTarget is not null || (fi.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                error = "Không cho phép đọc liên kết symbolic/reparse point.";
                return false;
            }
        }

        fullPath = candidate;
        return true;
    }

    public IReadOnlyList<VaultFileEntryDto> ListVaultFiles()
    {
        if (!Directory.Exists(_vaultRoot))
        {
            return [];
        }

        var results = new List<VaultFileEntryDto>();
        foreach (string file in Directory.EnumerateFiles(_vaultRoot, "*", SearchOption.AllDirectories))
        {
            var fi = new FileInfo(file);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            string rel = Path.GetRelativePath(_vaultRoot, file).Replace('\\', '/');
            results.Add(new VaultFileEntryDto(
                rel,
                fi.Length,
                new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero)));
        }

        return results.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static async Task<string> ComputeSha256HexAsync(string filePath, CancellationToken ct = default)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
