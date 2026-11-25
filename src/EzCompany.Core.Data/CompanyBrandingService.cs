using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

/// <summary>
/// Manages per-company logo branding stored in local SQLite (<c>CompanyProfiles</c>),
/// cached on disk for instant startup display, and synchronized from Director to all workstations.
/// </summary>
public sealed class CompanyBrandingService
{
    public const int MaxLogoBytes = 2 * 1024 * 1024;

    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly IAuditLogger _audit;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly string _logoCachePath;

    public CompanyBrandingService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        IAuditLogger audit,
        IEventBus eventBus,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _paths = paths;
        _audit = audit;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logoCachePath = Path.Combine(paths.ProfileRoot, "company-logo.png");
    }

    public string LogoCachePath => _logoCachePath;

    public async Task SaveCompanyLogoFromFileAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp hình ảnh logo.", filePath);
        }

        byte[] bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        await SaveCompanyLogoAsync(bytes, ct).ConfigureAwait(false);
    }

    public async Task SaveCompanyLogoAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0 || imageBytes.Length > MaxLogoBytes)
        {
            throw new ArgumentException("Kích thước logo phải lớn hơn 0 và nhỏ hơn 2 MB.", nameof(imageBytes));
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity company = await db.CompanyProfiles.FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Chưa khởi tạo hồ sơ công ty.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        company.CompanyLogoBase64 = Convert.ToBase64String(imageBytes);
        company.CompanyLogoUpdatedAtUtc = now;
        company.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(_logoCachePath)!);
        await File.WriteAllBytesAsync(_logoCachePath, imageBytes, ct).ConfigureAwait(false);

        await _audit.LogAsync("COMPANY_LOGO_UPDATED", "core.branding", company.CompanyName, $"Bytes={imageBytes.Length}", "Info", ct)
            .ConfigureAwait(false);
        _eventBus.Publish(new CompanyLogoUpdatedEvent(company.CompanyId, now));
    }

    public async Task RemoveCompanyLogoAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        company.CompanyLogoBase64 = null;
        company.CompanyLogoUpdatedAtUtc = now;
        company.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (File.Exists(_logoCachePath))
        {
            File.Delete(_logoCachePath);
        }

        await _audit.LogAsync("COMPANY_LOGO_REMOVED", "core.branding", company.CompanyName, null, "Info", ct)
            .ConfigureAwait(false);
        _eventBus.Publish(new CompanyLogoUpdatedEvent(company.CompanyId, now));
    }

    public async Task<byte[]?> GetCompanyLogoBytesAsync(CancellationToken ct = default)
    {
        if (File.Exists(_logoCachePath))
        {
            byte[] cached = await File.ReadAllBytesAsync(_logoCachePath, ct).ConfigureAwait(false);
            if (cached.Length > 0)
            {
                return cached;
            }
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(company?.CompanyLogoBase64))
        {
            return null;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(company.CompanyLogoBase64);
            Directory.CreateDirectory(Path.GetDirectoryName(_logoCachePath)!);
            await File.WriteAllBytesAsync(_logoCachePath, bytes, ct).ConfigureAwait(false);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(string? LogoBase64, DateTimeOffset? UpdatedAtUtc, Guid CompanyId)> GetCurrentBrandingTupleAsync(
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return company is null
            ? (null, null, Guid.Empty)
            : (company.CompanyLogoBase64, company.CompanyLogoUpdatedAtUtc, company.CompanyId);
    }

    public async Task<bool> ApplyIncomingLogoSyncAsync(
        Guid companyId,
        string? logoBase64,
        DateTimeOffset? updatedAtUtc,
        CancellationToken ct = default)
    {
        if (updatedAtUtc is null)
        {
            return false;
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || company.CompanyId != companyId)
        {
            return false;
        }

        if (company.CompanyLogoUpdatedAtUtc.HasValue && company.CompanyLogoUpdatedAtUtc.Value >= updatedAtUtc.Value)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(logoBase64))
        {
            company.CompanyLogoBase64 = null;
            company.CompanyLogoUpdatedAtUtc = updatedAtUtc;
            company.UpdatedAtUtc = _timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (File.Exists(_logoCachePath))
            {
                File.Delete(_logoCachePath);
            }
            _eventBus.Publish(new CompanyLogoUpdatedEvent(company.CompanyId, updatedAtUtc.Value));
            return true;
        }

        byte[] bytes = Convert.FromBase64String(logoBase64);
        if (bytes.Length > MaxLogoBytes)
        {
            return false;
        }

        company.CompanyLogoBase64 = logoBase64;
        company.CompanyLogoUpdatedAtUtc = updatedAtUtc;
        company.UpdatedAtUtc = _timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(_logoCachePath)!);
        await File.WriteAllBytesAsync(_logoCachePath, bytes, ct).ConfigureAwait(false);
        _eventBus.Publish(new CompanyLogoUpdatedEvent(company.CompanyId, updatedAtUtc.Value));
        return true;
    }
}
