using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public static class ExpenseRequestTypes
{
    public const string Advance = "Advance";
    public const string Reimbursement = "Reimbursement";
    public const string Payment = "Payment";

    public static readonly IReadOnlyList<string> All = [Advance, Reimbursement, Payment];
}

public static class ExpenseRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Disbursed = "Disbursed";
    public const string Rejected = "Rejected";
}

/// <summary>
/// Manages internal company Cashbook ledger entries and employee advance / reimbursement / payment requests.
/// </summary>
public sealed class AccountingLedgerService
{
    public const long MaxReceiptBytes = 10 * 1024 * 1024;

    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public AccountingLedgerService(
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

    public async Task<IReadOnlyList<CashbookEntryEntity>> GetCashbookEntriesAsync(
        int? year = null,
        int? month = null,
        string? typeFilter = null,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<CashbookEntryEntity> q = db.CashbookEntries.AsNoTracking();

        if (year.HasValue && month.HasValue)
        {
            string prefix = $"{year.Value:D4}-{month.Value:D2}-";
            q = q.Where(e => e.EntryDateKey.StartsWith(prefix));
        }

        if (!string.IsNullOrWhiteSpace(typeFilter) && !string.Equals(typeFilter, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(e => e.EntryType == typeFilter);
        }

        return await q
            .OrderByDescending(e => e.EntryDateKey)
            .ThenByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<CashbookEntryEntity> SaveCashbookEntryAsync(CashbookEntryEntity entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.AmountVnd <= 0)
        {
            throw new ArgumentException("Số tiền thu/chi phải lớn hơn 0.", nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.Description))
        {
            throw new ArgumentException("Vui lòng nhập diễn giải thu/chi.", nameof(entry));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        CashbookEntryEntity? existing = entry.Id != Guid.Empty
            ? await db.CashbookEntries.FirstOrDefaultAsync(x => x.Id == entry.Id, ct).ConfigureAwait(false)
            : null;

        if (existing is null)
        {
            entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
            entry.CompanyId = _session.CompanyId;
            entry.EntryType = string.Equals(entry.EntryType, "Income", StringComparison.OrdinalIgnoreCase) ? "Income" : "Expense";
            entry.PaymentMethod = string.Equals(entry.PaymentMethod, "BankTransfer", StringComparison.OrdinalIgnoreCase) ? "BankTransfer" : "Cash";
            entry.CreatedByName = string.IsNullOrWhiteSpace(entry.CreatedByName) ? _session.FullName : entry.CreatedByName;
            entry.CreatedAtUtc = now;
            entry.UpdatedAtUtc = now;
            db.CashbookEntries.Add(entry);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await _audit.LogAsync("CASHBOOK_ENTRY_ADDED", "module.accounting", entry.EntryType, $"{entry.AmountVnd:N0}đ - {entry.Description}", "Info", ct).ConfigureAwait(false);
            return entry;
        }

        existing.EntryDateKey = entry.EntryDateKey;
        existing.EntryType = string.Equals(entry.EntryType, "Income", StringComparison.OrdinalIgnoreCase) ? "Income" : "Expense";
        existing.Category = entry.Category;
        existing.AmountVnd = entry.AmountVnd;
        existing.Description = entry.Description;
        existing.ReferenceCode = entry.ReferenceCode;
        existing.PaymentMethod = string.Equals(entry.PaymentMethod, "BankTransfer", StringComparison.OrdinalIgnoreCase) ? "BankTransfer" : "Cash";
        existing.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return existing;
    }

    public async Task DeleteCashbookEntryAsync(Guid id, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CashbookEntryEntity? item = await db.CashbookEntries.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (item is null)
        {
            return;
        }

        db.CashbookEntries.Remove(item);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public static (decimal TotalIncome, decimal TotalExpense, decimal Balance) Summarize(IEnumerable<CashbookEntryEntity> entries)
    {
        decimal income = 0m;
        decimal expense = 0m;
        foreach (CashbookEntryEntity e in entries)
        {
            if (string.Equals(e.EntryType, "Income", StringComparison.OrdinalIgnoreCase))
            {
                income += e.AmountVnd;
            }
            else
            {
                expense += e.AmountVnd;
            }
        }

        return (income, expense, income - expense);
    }

    public async Task<ExpenseRequestEntity> SubmitExpenseRequestAsync(
        string requestType,
        string category,
        decimal amountVnd,
        string description,
        string? receiptSourcePath = null,
        CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
        {
            throw new InvalidOperationException("Phiên làm việc chưa đăng nhập.");
        }

        if (amountVnd <= 0)
        {
            throw new ArgumentException("Số tiền đề xuất phải lớn hơn 0.", nameof(amountVnd));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Vui lòng nhập nội dung đề xuất chi phí.", nameof(description));
        }

        Guid id = Guid.NewGuid();
        string? receiptName = null;
        string? receiptRelPath = null;
        if (!string.IsNullOrWhiteSpace(receiptSourcePath) && File.Exists(receiptSourcePath))
        {
            byte[] bytes = await File.ReadAllBytesAsync(receiptSourcePath, ct).ConfigureAwait(false);
            if (bytes.Length > MaxReceiptBytes)
            {
                throw new InvalidOperationException("Tệp chứng từ vượt quá 10 MB.");
            }

            receiptName = SanitizeFileName(Path.GetFileName(receiptSourcePath));
            string rel = Path.Combine("Accounting", "Receipts", $"{id:N}_{receiptName}").Replace('\\', '/');
            string dest = Path.Combine(_paths.VaultDirectory, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await File.WriteAllBytesAsync(dest, bytes, ct).ConfigureAwait(false);
            receiptRelPath = rel;
        }

        string normalizedType = ExpenseRequestTypes.All.FirstOrDefault(t =>
            string.Equals(t, requestType, StringComparison.OrdinalIgnoreCase)) ?? ExpenseRequestTypes.Advance;

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var req = new ExpenseRequestEntity
        {
            Id = id,
            CompanyId = _session.CompanyId,
            RequesterMachineId = _paths.MachineId,
            RequesterUsername = _session.Username,
            RequesterFullName = _session.FullName,
            RequestType = normalizedType,
            Category = string.IsNullOrWhiteSpace(category) ? "Chi phí khác" : category.Trim(),
            AmountVnd = amountVnd,
            Description = description.Trim(),
            ReceiptFileName = receiptName,
            ReceiptVaultPath = receiptRelPath,
            Status = ExpenseRequestStatuses.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.ExpenseRequests.Add(req);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync("EXPENSE_REQUEST_SUBMITTED", "module.accounting", req.RequesterUsername, $"{req.AmountVnd:N0}đ ({req.RequestType})", "Info", ct).ConfigureAwait(false);
        _eventBus.Publish(new ExpenseRequestUpdatedEvent(req.Id, req.RequesterFullName, req.Status, now));
        return req;
    }

    public async Task<IReadOnlyList<ExpenseRequestEntity>> GetExpenseRequestsAsync(
        bool pendingOnly = false,
        bool mineOnly = false,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<ExpenseRequestEntity> q = db.ExpenseRequests.AsNoTracking();
        if (pendingOnly)
        {
            q = q.Where(x => x.Status == ExpenseRequestStatuses.Pending);
        }

        if (mineOnly)
        {
            q = q.Where(x => x.RequesterMachineId == _paths.MachineId);
        }

        return await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ExpenseRequestEntity> DecideExpenseRequestAsync(
        Guid requestId,
        bool approve,
        string reviewNotes,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ExpenseRequestEntity req = await db.ExpenseRequests.FirstOrDefaultAsync(x => x.Id == requestId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy đề xuất chi phí.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        req.Status = approve ? ExpenseRequestStatuses.Approved : ExpenseRequestStatuses.Rejected;
        req.ApprovedByName = _session.FullName;
        req.ApprovedAtUtc = now;
        req.ReviewNotes = reviewNotes?.Trim() ?? string.Empty;
        req.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(
            approve ? "EXPENSE_APPROVED" : "EXPENSE_REJECTED",
            "module.accounting",
            req.RequesterUsername,
            $"{req.AmountVnd:N0}đ -> {req.Status}",
            "Info",
            ct).ConfigureAwait(false);

        _eventBus.Publish(new ExpenseRequestUpdatedEvent(req.Id, req.RequesterFullName, req.Status, now));
        return req;
    }

    public async Task<(ExpenseRequestEntity Request, CashbookEntryEntity LedgerEntry)> DisburseExpenseRequestAsync(
        Guid requestId,
        string paymentMethod = "BankTransfer",
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ExpenseRequestEntity req = await db.ExpenseRequests.FirstOrDefaultAsync(x => x.Id == requestId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy đề xuất chi phí.");

        if (!string.Equals(req.Status, ExpenseRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Chỉ được chi tiền cho đề xuất đã được Giám đốc phê duyệt.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        req.Status = ExpenseRequestStatuses.Disbursed;
        req.UpdatedAtUtc = now;

        var ledgerEntry = new CashbookEntryEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = req.CompanyId,
            EntryDateKey = now.ToLocalTime().ToString("yyyy-MM-dd"),
            EntryType = "Expense",
            Category = req.Category,
            AmountVnd = req.AmountVnd,
            Description = $"Chi [{req.RequestType}] cho {req.RequesterFullName}: {req.Description}",
            ReferenceCode = $"PC-{now:yyyyMMdd}-{req.Id.ToString("N")[..4].ToUpperInvariant()}",
            PaymentMethod = string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase) ? "Cash" : "BankTransfer",
            LinkedExpenseRequestId = req.Id,
            CreatedByName = _session.FullName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.CashbookEntries.Add(ledgerEntry);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync("EXPENSE_DISBURSED", "module.accounting", req.RequesterUsername, $"{req.AmountVnd:N0}đ ({ledgerEntry.ReferenceCode})", "Info", ct).ConfigureAwait(false);
        _eventBus.Publish(new ExpenseRequestUpdatedEvent(req.Id, req.RequesterFullName, req.Status, now));
        return (req, ledgerEntry);
    }

    public async Task DeleteInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        AccountingInvoiceEntity? inv = await db.AccountingInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct).ConfigureAwait(false);
        if (inv is not null)
        {
            db.AccountingInvoices.Remove(inv);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ExpenseRequestEntity>> GetAllExpensesForSyncAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ExpenseRequests.AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(200)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> UpsertIncomingExpensesAsync(IEnumerable<ExpenseRequestEntity> incoming, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        int changes = 0;
        foreach (ExpenseRequestEntity item in incoming)
        {
            ExpenseRequestEntity? existing = await db.ExpenseRequests.FirstOrDefaultAsync(x => x.Id == item.Id, ct).ConfigureAwait(false);
            if (existing is null)
            {
                db.ExpenseRequests.Add(item);
                changes++;
                _eventBus.Publish(new ExpenseRequestUpdatedEvent(item.Id, item.RequesterFullName, item.Status, item.UpdatedAtUtc));
            }
            else if (item.UpdatedAtUtc > existing.UpdatedAtUtc)
            {
                existing.Status = item.Status;
                existing.ApprovedByName = item.ApprovedByName;
                existing.ApprovedAtUtc = item.ApprovedAtUtc;
                existing.ReviewNotes = item.ReviewNotes;
                existing.UpdatedAtUtc = item.UpdatedAtUtc;
                changes++;
                _eventBus.Publish(new ExpenseRequestUpdatedEvent(existing.Id, existing.RequesterFullName, existing.Status, existing.UpdatedAtUtc));
            }
        }

        if (changes > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return changes;
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
