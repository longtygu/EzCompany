using System.Security.Cryptography;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public static class SharedDocumentCategories
{
    public const string Policy = "Policy";
    public const string Template = "Template";
    public const string Notice = "Notice";
    public const string General = "General";

    public static readonly IReadOnlyList<string> All = [Policy, Template, Notice, General];
}

/// <summary>
/// Manages the shared company LAN document repository with SHA-256 verification and P2P file pull.
/// </summary>
public sealed class SharedDocumentService
{
    public const long MaxSharedDocBytes = 15 * 1024 * 1024;

    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public SharedDocumentService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        IAuditLogger audit,
        IEventBus eventBus,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
        _audit = audit;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SharedDocumentEntity> UploadDocumentAsync(
        string title,
        string category,
        string description,
        string sourceFilePath,
        CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
        {
            throw new InvalidOperationException("Phiên làm việc chưa đăng nhập.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Vui lòng nhập tên tài liệu.", nameof(title));
        }

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp tải lên.", sourceFilePath);
        }

        byte[] bytes = await File.ReadAllBytesAsync(sourceFilePath, ct).ConfigureAwait(false);
        if (bytes.Length > MaxSharedDocBytes)
        {
            throw new InvalidOperationException("Tài liệu vượt quá giới hạn 15 MB.");
        }

        Guid id = Guid.NewGuid();
        string safeName = SanitizeFileName(Path.GetFileName(sourceFilePath));
        string relPath = Path.Combine("SharedDocs", $"{id:N}_{safeName}").Replace('\\', '/');
        string dest = Path.Combine(_paths.VaultDirectory, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllBytesAsync(dest, bytes, ct).ConfigureAwait(false);

        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string normalizedCat = SharedDocumentCategories.All.FirstOrDefault(c =>
            string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) ?? SharedDocumentCategories.General;

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var doc = new SharedDocumentEntity
        {
            Id = id,
            CompanyId = _session.CompanyId,
            Title = title.Trim(),
            Category = normalizedCat,
            Description = description?.Trim() ?? string.Empty,
            FileName = safeName,
            VaultRelativePath = relPath,
            FileSha256 = sha256,
            FileSizeBytes = bytes.LongLength,
            UploadedByMachineId = _paths.MachineId,
            UploadedByName = _session.FullName,
            UploadedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.SharedDocuments.Add(doc);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync("SHARED_DOC_UPLOADED", "core.shareddocs", doc.Title, $"{doc.FileName} ({doc.FileSizeBytes} B)", "Info", ct).ConfigureAwait(false);
        _eventBus.Publish(new SharedDocumentUpdatedEvent(doc.Id, doc.Title, now));
        return doc;
    }

    public async Task<IReadOnlyList<SharedDocumentEntity>> GetActiveDocumentsAsync(
        string? category = null,
        string? search = null,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<SharedDocumentEntity> q = db.SharedDocuments.AsNoTracking().Where(d => !d.IsDeleted);

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(d => d.Category == category);
        }

        List<SharedDocumentEntity> list = await q
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            list = list.Where(d =>
                d.Title.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                d.FileName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                d.Description.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return list;
    }

    public async Task SoftDeleteDocumentAsync(Guid docId, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        SharedDocumentEntity? doc = await db.SharedDocuments.FirstOrDefaultAsync(d => d.Id == docId, ct).ConfigureAwait(false);
        if (doc is null)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        doc.IsDeleted = true;
        doc.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _eventBus.Publish(new SharedDocumentUpdatedEvent(doc.Id, doc.Title, now));
    }

    public async Task<IReadOnlyList<SharedDocumentEntity>> GetAllForSyncAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.SharedDocuments.AsNoTracking()
            .OrderByDescending(d => d.UpdatedAtUtc)
            .Take(300)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> UpsertIncomingDocumentsAsync(IEnumerable<SharedDocumentEntity> incoming, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        int changes = 0;
        foreach (SharedDocumentEntity item in incoming)
        {
            SharedDocumentEntity? existing = await db.SharedDocuments.FirstOrDefaultAsync(d => d.Id == item.Id, ct).ConfigureAwait(false);
            if (existing is null)
            {
                db.SharedDocuments.Add(new SharedDocumentEntity
                {
                    Id = item.Id,
                    CompanyId = item.CompanyId,
                    Title = item.Title,
                    Category = item.Category,
                    Description = item.Description,
                    FileName = item.FileName,
                    VaultRelativePath = null, // File will be pulled on demand or kept null until downloaded locally
                    FileSha256 = item.FileSha256,
                    FileSizeBytes = item.FileSizeBytes,
                    UploadedByMachineId = item.UploadedByMachineId,
                    UploadedByName = item.UploadedByName,
                    UploadedAtUtc = item.UploadedAtUtc,
                    IsDeleted = item.IsDeleted,
                    UpdatedAtUtc = item.UpdatedAtUtc
                });
                changes++;
                _eventBus.Publish(new SharedDocumentUpdatedEvent(item.Id, item.Title, item.UpdatedAtUtc));
            }
            else if (item.UpdatedAtUtc > existing.UpdatedAtUtc)
            {
                existing.Title = item.Title;
                existing.Category = item.Category;
                existing.Description = item.Description;
                existing.IsDeleted = item.IsDeleted;
                existing.UpdatedAtUtc = item.UpdatedAtUtc;
                changes++;
                _eventBus.Publish(new SharedDocumentUpdatedEvent(existing.Id, existing.Title, existing.UpdatedAtUtc));
            }
        }

        if (changes > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return changes;
    }

    public async Task<string> SaveDownloadedFileAsync(Guid docId, byte[] bytes, string? expectedSha256 = null, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        SharedDocumentEntity doc = await db.SharedDocuments.FirstOrDefaultAsync(d => d.Id == docId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy tài liệu trong danh mục.");

        string shaToCheck = string.IsNullOrWhiteSpace(expectedSha256) ? doc.FileSha256 : expectedSha256;
        string actualSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(shaToCheck) &&
            !string.Equals(actualSha, shaToCheck, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException("SHA-256 của tài liệu tải về không khớp với bản gốc.");
        }

        string safeName = SanitizeFileName(doc.FileName);
        string relPath = Path.Combine("SharedDocs", $"{doc.Id:N}_{safeName}").Replace('\\', '/');
        string fullDest = Path.Combine(_paths.VaultDirectory, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullDest)!);
        await File.WriteAllBytesAsync(fullDest, bytes, ct).ConfigureAwait(false);

        doc.VaultRelativePath = relPath;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return fullDest;
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
