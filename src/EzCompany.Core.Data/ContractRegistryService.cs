using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

/// <summary>
/// Manages the Legal Contract Registry with expiration monitoring and automatic Timeline milestone creation.
/// </summary>
public sealed class ContractRegistryService
{
    public const long MaxContractFileBytes = 15 * 1024 * 1024;

    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly TimelinePlanService _timelineService;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _timeProvider;

    public ContractRegistryService(
        IDbContextFactory<AppDbContext> appDbFactory,
        TimelinePlanService timelineService,
        ILocalStoragePaths paths,
        ISessionContext session,
        IAuditLogger audit,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _timelineService = timelineService;
        _paths = paths;
        _session = session;
        _audit = audit;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static string ComputeStatus(DateOnly expiryDate, DateOnly today, string currentStatus)
    {
        if (string.Equals(currentStatus, "Terminated", StringComparison.OrdinalIgnoreCase))
        {
            return "Terminated";
        }

        if (expiryDate < today)
        {
            return "Expired";
        }

        if (expiryDate <= today.AddDays(30))
        {
            return "ExpiringSoon";
        }

        return "Active";
    }

    public async Task<IReadOnlyList<ContractRecordEntity>> GetContractsAsync(
        string? statusFilter = null,
        string? search = null,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<ContractRecordEntity> list = await db.ContractRecords.AsNoTracking()
            .OrderBy(c => c.ExpiryDateKey)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        foreach (ContractRecordEntity c in list)
        {
            if (DateOnly.TryParse(c.ExpiryDateKey, out DateOnly exp))
            {
                c.Status = ComputeStatus(exp, today, c.Status);
            }
        }

        IEnumerable<ContractRecordEntity> filtered = list;
        if (!string.IsNullOrWhiteSpace(statusFilter) && !string.Equals(statusFilter, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(c => string.Equals(c.Status, statusFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim();
            filtered = filtered.Where(c =>
                c.ContractNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.CounterpartyName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToArray();
    }

    public async Task<ContractRecordEntity> SaveContractAsync(
        ContractRecordEntity contract,
        string? attachSourcePath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (string.IsNullOrWhiteSpace(contract.ContractNumber))
        {
            throw new ArgumentException("Vui lòng nhập số hợp đồng.", nameof(contract));
        }

        if (string.IsNullOrWhiteSpace(contract.Title))
        {
            throw new ArgumentException("Vui lòng nhập tên hợp đồng.", nameof(contract));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        string computedStatus = DateOnly.TryParse(contract.ExpiryDateKey, out DateOnly exp)
            ? ComputeStatus(exp, today, contract.Status)
            : contract.Status;

        Guid id = contract.Id == Guid.Empty ? Guid.NewGuid() : contract.Id;
        string? fileName = contract.FileName;
        string? vaultRelPath = contract.VaultFilePath;
        if (!string.IsNullOrWhiteSpace(attachSourcePath) && File.Exists(attachSourcePath))
        {
            byte[] bytes = await File.ReadAllBytesAsync(attachSourcePath, ct).ConfigureAwait(false);
            if (bytes.Length > MaxContractFileBytes)
            {
                throw new InvalidOperationException("Tệp hợp đồng vượt quá 15 MB.");
            }

            fileName = SanitizeFileName(Path.GetFileName(attachSourcePath));
            vaultRelPath = Path.Combine("Legal", "Contracts", $"{id:N}_{fileName}").Replace('\\', '/');
            string dest = Path.Combine(_paths.VaultDirectory, vaultRelPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await File.WriteAllBytesAsync(dest, bytes, ct).ConfigureAwait(false);
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ContractRecordEntity? existing = await db.ContractRecords.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);

        if (existing is null)
        {
            contract.Id = id;
            contract.CompanyId = _session.CompanyId;
            contract.Status = computedStatus;
            contract.FileName = fileName;
            contract.VaultFilePath = vaultRelPath;
            contract.OwnerName = string.IsNullOrWhiteSpace(contract.OwnerName) ? _session.FullName : contract.OwnerName;
            contract.CreatedAtUtc = now;
            contract.UpdatedAtUtc = now;
            db.ContractRecords.Add(contract);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await _audit.LogAsync("CONTRACT_ADDED", "module.legal", contract.ContractNumber, contract.Title, "Info", ct).ConfigureAwait(false);
            return contract;
        }

        existing.ContractNumber = contract.ContractNumber.Trim();
        existing.Title = contract.Title.Trim();
        existing.CounterpartyName = contract.CounterpartyName.Trim();
        existing.ContractType = contract.ContractType;
        existing.SignedDateKey = contract.SignedDateKey;
        existing.StartDateKey = contract.StartDateKey;
        existing.ExpiryDateKey = contract.ExpiryDateKey;
        existing.ValueVnd = contract.ValueVnd;
        existing.Status = computedStatus;
        existing.OwnerName = contract.OwnerName;
        existing.FileName = fileName ?? existing.FileName;
        existing.VaultFilePath = vaultRelPath ?? existing.VaultFilePath;
        existing.Notes = contract.Notes;
        existing.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return existing;
    }

    public async Task DeleteContractAsync(Guid id, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ContractRecordEntity? item = await db.ContractRecords.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (item is not null)
        {
            db.ContractRecords.Remove(item);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<TimelinePlanEntity> SyncExpiryToTimelineAsync(Guid contractId, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ContractRecordEntity contract = await db.ContractRecords.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contractId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy hợp đồng.");

        if (!DateOnly.TryParse(contract.ExpiryDateKey, out DateOnly expiry))
        {
            throw new InvalidOperationException("Ngày hết hạn hợp đồng không hợp lệ.");
        }

        DateOnly reminderStart = expiry.AddDays(-7);
        DateTimeOffset startUtc = new DateTimeOffset(reminderStart.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);
        DateTimeOffset endUtc = new DateTimeOffset(expiry.ToDateTime(new TimeOnly(17, 30)), TimeSpan.Zero);

        return await _timelineService.AddOrUpdatePlanAsync(
            contract.Id,
            $"Hết hạn HĐ {contract.ContractNumber}: {contract.Title} ({contract.CounterpartyName})",
            $"Loại: {contract.ContractType} • Giá trị: {contract.ValueVnd:N0}đ • Ngày hết hạn: {expiry:dd/MM/yyyy}",
            TimelineCategories.Deadline,
            startUtc,
            endUtc,
            "Planned",
            ct).ConfigureAwait(false);
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
