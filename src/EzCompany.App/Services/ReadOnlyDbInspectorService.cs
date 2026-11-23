using System.IO;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.App.Services;

public sealed record InspectedDatabaseSnapshot(
    string FilePath,
    CompanyProfileEntity? CompanyProfile,
    IReadOnlyList<AccountingInvoiceEntity> Invoices,
    IReadOnlyList<LegalContractReviewEntity> ContractReviews,
    IReadOnlyList<OfficeDocumentRecordEntity> OfficeRecords,
    IReadOnlyList<AuditLogEntity> AuditLogs);

/// <summary>
/// Opens copied workstation <c>.db</c> snapshots strictly in <c>Mode=ReadOnly</c>
/// so Directors can inspect employee accounting/legal records without altering the snapshot
/// or mixing rows into the Director's local <c>app.db</c>.
/// </summary>
public sealed class ReadOnlyDbInspectorService
{
    public async Task<InspectedDatabaseSnapshot> InspectAsync(string dbFilePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dbFilePath) || !File.Exists(dbFilePath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp bản chụp CSDL (.db).", dbFilePath);
        }

        string connString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(dbFilePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite(connString);

        await using var db = new AppDbContext(optionsBuilder.Options);
        CompanyProfileEntity? profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        List<AccountingInvoiceEntity> invoices = await db.AccountingInvoices.AsNoTracking()
            .OrderByDescending(x => x.ImportedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        List<LegalContractReviewEntity> reviews = await db.LegalContractReviews.AsNoTracking()
            .OrderByDescending(x => x.ReviewedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        List<OfficeDocumentRecordEntity> office = await db.OfficeDocumentRecords.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        List<AuditLogEntity> audits = await db.AuditLogs.AsNoTracking()
            .OrderByDescending(x => x.TimestampUtc)
            .Take(200)
            .ToListAsync(ct).ConfigureAwait(false);

        return new InspectedDatabaseSnapshot(dbFilePath, profile, invoices, reviews, office, audits);
    }
}
