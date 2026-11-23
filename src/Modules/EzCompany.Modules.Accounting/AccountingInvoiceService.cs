using System.IO;
using System.Security.Cryptography;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Modules.Accounting;

public sealed class AccountingInvoiceService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly VietnameseInvoiceXmlParser _parser = new();

    public AccountingInvoiceService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        IAuditLogger audit)
    {
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AccountingInvoiceEntity>> ImportBatchAsync(
        IEnumerable<string> xmlFilePaths,
        CancellationToken ct = default)
    {
        List<string> files = xmlFilePaths.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            return [];
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        HashSet<string> existingKeys = (await db.AccountingInvoices
            .AsNoTracking()
            .Select(x => $"{x.SellerTaxCode}|{x.InvoiceSeries}|{x.InvoiceNumber}")
            .ToListAsync(ct)
            .ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var imported = new List<AccountingInvoiceEntity>();
        var batchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in files)
        {
            ParsedInvoiceXml parsed = _parser.ParseFile(path);
            byte[] fileBytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            string sha256 = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

            string invoiceKey = $"{parsed.SellerTaxCode}|{parsed.InvoiceSeries}|{parsed.InvoiceNumber}";
            bool isDup = !batchKeys.Add(invoiceKey) || existingKeys.Contains(invoiceKey);

            string monthDir = parsed.IssueDate.ToString("yyyyMM");
            string safeSeller = string.IsNullOrWhiteSpace(parsed.SellerTaxCode) ? "NOMST" : parsed.SellerTaxCode;
            string safeSeries = SanitizeFileName(parsed.InvoiceSeries);
            string safeNum = SanitizeFileName(parsed.InvoiceNumber);

            string relVaultPath = $"Accounting/Invoices/{monthDir}/{safeSeller}_{safeSeries}_{safeNum}_{sha256[..8]}.xml";
            string destFullPath = Path.Combine(_paths.VaultDirectory, relVaultPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);
            await File.WriteAllBytesAsync(destFullPath, fileBytes, ct).ConfigureAwait(false);

            string notes = parsed.ValidationSummary;
            if (isDup)
            {
                notes = notes == "Hợp lệ" ? "Cảnh báo: Trùng số hóa đơn / MST người bán" : $"Trùng hóa đơn; {notes}";
            }

            var entity = new AccountingInvoiceEntity
            {
                Id = Guid.NewGuid(),
                InvoiceSeries = parsed.InvoiceSeries,
                InvoiceNumber = parsed.InvoiceNumber,
                IssueDate = parsed.IssueDate,
                SellerTaxCode = parsed.SellerTaxCode,
                SellerName = parsed.SellerName,
                BuyerTaxCodeOrCccd = parsed.BuyerTaxCodeOrCccd,
                BuyerName = parsed.BuyerName,
                SubTotal = parsed.SubTotal,
                VatAmount = parsed.VatAmount,
                TotalAmount = parsed.TotalAmount,
                AmountInWords = VietnameseNumberToWordsConverter.Convert(parsed.TotalAmount),
                HasMathMismatch = parsed.HasMathMismatch,
                MathDifference = parsed.MathDifference,
                IsDuplicate = isDup,
                ValidationNotes = notes,
                VaultRelativePath = relVaultPath,
                FileSha256 = sha256,
                ImportedByUsername = string.IsNullOrWhiteSpace(_session.Username) ? "system" : _session.Username,
                ImportedAtUtc = DateTimeOffset.UtcNow
            };

            db.AccountingInvoices.Add(entity);
            imported.Add(entity);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(
            "ACCOUNTING_IMPORT_XML_BATCH",
            ModuleIds.AccountingInvoiceXml,
            $"{imported.Count} hóa đơn",
            $"Duplicates={imported.Count(x => x.IsDuplicate)};MathMismatch={imported.Count(x => x.HasMathMismatch)}",
            imported.Any(x => x.IsDuplicate || x.HasMathMismatch) ? "Warning" : "Info",
            ct).ConfigureAwait(false);

        return imported;
    }

    public async Task<IReadOnlyList<AccountingInvoiceEntity>> GetAllInvoicesAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AccountingInvoices
            .AsNoTracking()
            .OrderByDescending(x => x.ImportedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "EMPTY";
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(input.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
