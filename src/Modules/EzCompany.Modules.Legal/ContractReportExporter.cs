using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Modules.Legal;

public sealed class ContractReportExporter
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;

    public ContractReportExporter(
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

    public async Task<LegalContractReviewEntity> SaveToVaultAndDatabaseAsync(
        string title,
        string originalPath,
        string revisedPath,
        ContractDiffResult diff,
        string reviewerNotes,
        string? exportedReportFullPath = null,
        CancellationToken ct = default)
    {
        byte[] origBytes = await File.ReadAllBytesAsync(originalPath, ct).ConfigureAwait(false);
        byte[] revBytes = await File.ReadAllBytesAsync(revisedPath, ct).ConfigureAwait(false);

        string origSha = Convert.ToHexString(SHA256.HashData(origBytes)).ToLowerInvariant();
        string revSha = Convert.ToHexString(SHA256.HashData(revBytes)).ToLowerInvariant();

        string folder = $"Legal/Contracts/{DateTime.UtcNow:yyyyMM}";
        string origRel = $"{folder}/{Path.GetFileNameWithoutExtension(originalPath)}_{origSha[..8]}.docx";
        string revRel = $"{folder}/{Path.GetFileNameWithoutExtension(revisedPath)}_{revSha[..8]}.docx";

        string origDest = Path.Combine(_paths.VaultDirectory, origRel.Replace('/', Path.DirectorySeparatorChar));
        string revDest = Path.Combine(_paths.VaultDirectory, revRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(origDest)!);

        await File.WriteAllBytesAsync(origDest, origBytes, ct).ConfigureAwait(false);
        await File.WriteAllBytesAsync(revDest, revBytes, ct).ConfigureAwait(false);

        string reportRel = string.Empty;
        if (!string.IsNullOrWhiteSpace(exportedReportFullPath) && File.Exists(exportedReportFullPath))
        {
            reportRel = $"{folder}/{Path.GetFileName(exportedReportFullPath)}";
            string reportVaultDest = Path.Combine(_paths.VaultDirectory, reportRel.Replace('/', Path.DirectorySeparatorChar));
            File.Copy(exportedReportFullPath, reportVaultDest, overwrite: true);
        }

        var entity = new LegalContractReviewEntity
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(revisedPath) : title.Trim(),
            OriginalFileName = Path.GetFileName(originalPath),
            RevisedFileName = Path.GetFileName(revisedPath),
            OriginalVaultPath = origRel,
            RevisedVaultPath = revRel,
            OriginalSha256 = origSha,
            RevisedSha256 = revSha,
            AddedCount = diff.AddedCount,
            DeletedCount = diff.DeletedCount,
            ModifiedCount = diff.ModifiedCount,
            ReviewerNotes = reviewerNotes ?? string.Empty,
            ReportVaultPath = reportRel,
            ReviewedByUsername = string.IsNullOrWhiteSpace(_session.Username) ? "system" : _session.Username,
            ReviewedAtUtc = DateTimeOffset.UtcNow
        };

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.LegalContractReviews.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(
            "LEGAL_CONTRACT_REVIEW_SAVED",
            ModuleIds.LegalContractDiff,
            entity.Title,
            $"Added={diff.AddedCount};Deleted={diff.DeletedCount};Modified={diff.ModifiedCount};OrigSha={origSha[..12]};RevSha={revSha[..12]}",
            "Info",
            ct).ConfigureAwait(false);

        return entity;
    }

    public async Task ExportHtmlReportAsync(
        string outputHtmlPath,
        string title,
        string originalName,
        string revisedName,
        ContractDiffResult diff,
        string reviewerNotes,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputHtmlPath))!);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"vi\"><head><meta charset=\"utf-8\" />");
        sb.AppendLine($"<title>{WebUtility.HtmlEncode(title)}</title>");
        sb.AppendLine("<style>body{font-family:'Segoe UI',Arial,sans-serif;margin:24px;background:#f8f9fa;color:#222;}table{width:100%;border-collapse:collapse;background:#fff;}th,td{border:1px solid #ddd;padding:6px 10px;vertical-align:top;font-size:13px;}th{background:#e9ecef;}.added{background:#d4edda;}.deleted{background:#f8d7da;text-decoration:line-through;}.modified{background:#fff3cd;}</style></head><body>");
        sb.AppendLine($"<h2>Báo cáo So sánh Hợp đồng: {WebUtility.HtmlEncode(title)}</h2>");
        sb.AppendLine($"<p><b>Bản gốc:</b> {WebUtility.HtmlEncode(originalName)} &nbsp;|&nbsp; <b>Bản sửa:</b> {WebUtility.HtmlEncode(revisedName)}</p>");
        sb.AppendLine($"<p><b>Thống kê:</b> Thêm mới: {diff.AddedCount} | Xóa bỏ: {diff.DeletedCount} | Sửa đổi: {diff.ModifiedCount}</p>");
        sb.AppendLine($"<p><b>Ghi chú thẩm định:</b> {WebUtility.HtmlEncode(reviewerNotes)}</p>");
        sb.AppendLine("<table><thead><tr><th>#</th><th>Bản gốc</th><th>Bản sửa đổi</th></tr></thead><tbody>");

        foreach (SideBySideDiffLine line in diff.SideBySideLines)
        {
            string cls = line.Kind switch
            {
                ContractDiffKind.Added => "added",
                ContractDiffKind.Deleted => "deleted",
                ContractDiffKind.Modified => "modified",
                _ => ""
            };

            sb.AppendLine($"<tr class=\"{cls}\"><td>{line.Index + 1}</td><td>{WebUtility.HtmlEncode(line.OldText)}</td><td>{WebUtility.HtmlEncode(line.NewText)}</td></tr>");
        }

        sb.AppendLine("</tbody></table></body></html>");
        await File.WriteAllTextAsync(outputHtmlPath, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
    }

    public Task ExportDocxReportAsync(
        string outputDocxPath,
        string title,
        string originalName,
        string revisedName,
        ContractDiffResult diff,
        string reviewerNotes,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputDocxPath))!);
        using WordprocessingDocument doc = WordprocessingDocument.Create(outputDocxPath, WordprocessingDocumentType.Document);
        MainDocumentPart main = doc.AddMainDocumentPart();
        main.Document = new Document();
        Body body = main.Document.AppendChild(new Body());

        AppendParagraph(body, $"BÁO CÁO THẨM ĐỊNH & SO SÁNH HỢP ĐỒNG: {title}", bold: true);
        AppendParagraph(body, $"Bản gốc: {originalName} | Bản sửa đổi: {revisedName}");
        AppendParagraph(body, $"Thay đổi: +{diff.AddedCount} thêm, -{diff.DeletedCount} xóa, ~{diff.ModifiedCount} sửa");
        AppendParagraph(body, $"Ghi chú thẩm định: {reviewerNotes}");
        AppendParagraph(body, "Chi tiết các điều khoản thay đổi:", bold: true);

        foreach (SideBySideDiffLine line in diff.SideBySideLines.Where(l => l.Kind != ContractDiffKind.Unchanged))
        {
            string label = line.Kind switch
            {
                ContractDiffKind.Added => "[THÊM]",
                ContractDiffKind.Deleted => "[XÓA]",
                _ => "[SỬA]"
            };
            AppendParagraph(body, $"Dòng {line.Index + 1} {label}: Gốc=\"{line.OldText}\" -> Mới=\"{line.NewText}\"");
        }

        main.Document.Save();
        return Task.CompletedTask;
    }

    private static void AppendParagraph(Body body, string text, bool bold = false)
    {
        var run = new Run(new Text(text));
        if (bold)
        {
            run.RunProperties = new RunProperties(new Bold());
        }

        body.AppendChild(new Paragraph(run));
    }
}
