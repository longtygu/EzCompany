using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace EzCompany.Modules.Office;

public sealed record PdfInspectionResult(int PageCount, string TextSample);

public sealed record PdfWatermarkOptions(
    string Text,
    bool IncludeTimestamp = true,
    bool IncludeUsername = true,
    double Opacity = 0.25,
    double FontSize = 36,
    string ColorHex = "#B00020",
    double AngleDegrees = -45);

public sealed record PdfOperationResult(
    string OutputPath,
    string? VaultRelativePath,
    string Sha256Hex,
    int PageCount);

public sealed class LocalPdfStudioService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;

    public LocalPdfStudioService(
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

    public PdfInspectionResult InspectPdf(string pdfPath)
    {
        using PdfPigDocument doc = PdfPigDocument.Open(pdfPath);
        int count = doc.NumberOfPages;
        var sb = new StringBuilder();
        for (int i = 1; i <= Math.Min(count, 3); i++)
        {
            sb.AppendLine(doc.GetPage(i).Text);
        }

        return new PdfInspectionResult(count, sb.ToString().Trim());
    }

    public static IReadOnlyList<int> ParsePageRanges(string? rangeSpec, int maxPages)
    {
        if (maxPages <= 0)
        {
            throw new ArgumentException("Tệp PDF không có trang nào.", nameof(maxPages));
        }

        if (string.IsNullOrWhiteSpace(rangeSpec))
        {
            return Enumerable.Range(1, maxPages).ToArray();
        }

        var pages = new List<int>();
        string[] tokens = rangeSpec.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            throw new ArgumentException("Khoảng trang không hợp lệ.", nameof(rangeSpec));
        }

        foreach (string token in tokens)
        {
            int dashIdx = token.IndexOf('-');
            if (dashIdx < 0)
            {
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int single) ||
                    single < 1 || single > maxPages)
                {
                    throw new ArgumentException($"Số trang '{token}' nằm ngoài phạm vi 1..{maxPages}.", nameof(rangeSpec));
                }

                pages.Add(single);
            }
            else
            {
                string left = token[..dashIdx].Trim();
                string right = token[(dashIdx + 1)..].Trim();
                if (!int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out int start) ||
                    !int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out int end) ||
                    start < 1 || end > maxPages || start > end)
                {
                    throw new ArgumentException($"Khoảng trang '{token}' không hợp lệ (1..{maxPages}).", nameof(rangeSpec));
                }

                for (int p = start; p <= end; p++)
                {
                    pages.Add(p);
                }
            }
        }

        return pages;
    }

    public async Task<PdfOperationResult> MergePdfsAsync(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        bool saveToVault = false,
        CancellationToken ct = default)
    {
        if (inputPaths is null || inputPaths.Count == 0)
        {
            throw new ArgumentException("Chọn ít nhất 1 tệp PDF để gộp.", nameof(inputPaths));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var outputDoc = new PdfDocument();

        foreach (string path in inputPaths)
        {
            using PdfDocument inputDoc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            for (int i = 0; i < inputDoc.PageCount; i++)
            {
                outputDoc.AddPage(inputDoc.Pages[i]);
            }
        }

        int pageCount = outputDoc.PageCount;
        outputDoc.Save(outputPath);
        return await RecordOperationAsync("Merge", inputPaths, outputPath, pageCount, saveToVault, string.Empty, ct)
            .ConfigureAwait(false);
    }

    public async Task<PdfOperationResult> SplitPdfAsync(
        string inputPath,
        string pageRangeSpec,
        string outputPath,
        bool saveToVault = false,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using PdfDocument inputDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        IReadOnlyList<int> selectedPages = ParsePageRanges(pageRangeSpec, inputDoc.PageCount);

        using var outputDoc = new PdfDocument();
        foreach (int pageNumber in selectedPages)
        {
            outputDoc.AddPage(inputDoc.Pages[pageNumber - 1]);
        }

        int pageCount = outputDoc.PageCount;
        outputDoc.Save(outputPath);
        return await RecordOperationAsync("Split", [inputPath], outputPath, pageCount, saveToVault, $"Pages={pageRangeSpec}", ct)
            .ConfigureAwait(false);
    }

    public async Task<PdfOperationResult> RotatePdfAsync(
        string inputPath,
        int degrees,
        string? pageRangeSpec,
        string outputPath,
        bool saveToVault = false,
        CancellationToken ct = default)
    {
        if (degrees is not (90 or 180 or 270))
        {
            throw new ArgumentException("Góc xoay phải là 90, 180 hoặc 270 độ.", nameof(degrees));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using PdfDocument inputDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        HashSet<int> targets = ParsePageRanges(pageRangeSpec, inputDoc.PageCount).ToHashSet();

        using var outputDoc = new PdfDocument();
        for (int i = 0; i < inputDoc.PageCount; i++)
        {
            PdfPage added = outputDoc.AddPage(inputDoc.Pages[i]);
            if (targets.Contains(i + 1))
            {
                added.Rotate = (added.Rotate + degrees) % 360;
            }
        }

        int pageCount = outputDoc.PageCount;
        outputDoc.Save(outputPath);
        return await RecordOperationAsync("Rotate", [inputPath], outputPath, pageCount, saveToVault, $"Deg={degrees}", ct)
            .ConfigureAwait(false);
    }

    public async Task<PdfOperationResult> WatermarkPdfAsync(
        string inputPath,
        PdfWatermarkOptions options,
        string outputPath,
        bool saveToVault = false,
        CancellationToken ct = default)
    {
        WindowsPdfFontResolver.EnsureInitialized();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using PdfDocument inputDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        using var outputDoc = new PdfDocument();

        for (int i = 0; i < inputDoc.PageCount; i++)
        {
            outputDoc.AddPage(inputDoc.Pages[i]);
        }

        string stampText = BuildWatermarkText(options);
        XColor color = ParseHexColor(options.ColorHex, options.Opacity);
        var brush = new XSolidBrush(color);
        var font = new XFont("Arial", Math.Clamp(options.FontSize, 10, 120), XFontStyleEx.Bold);

        foreach (PdfPage page in outputDoc.Pages)
        {
            using XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            double cx = page.Width.Point / 2.0;
            double cy = page.Height.Point / 2.0;

            gfx.TranslateTransform(cx, cy);
            gfx.RotateTransform(options.AngleDegrees);

            XSize size = gfx.MeasureString(stampText, font);
            gfx.DrawString(
                stampText,
                font,
                brush,
                new XPoint(-size.Width / 2.0, size.Height / 4.0));
        }

        int pageCount = outputDoc.PageCount;
        outputDoc.Save(outputPath);
        return await RecordOperationAsync("Watermark", [inputPath], outputPath, pageCount, saveToVault, stampText, ct)
            .ConfigureAwait(false);
    }

    public async Task<PdfOperationResult> ConvertImagesToPdfAsync(
        IReadOnlyList<string> imagePaths,
        string outputPath,
        bool fitToA4 = true,
        bool saveToVault = false,
        CancellationToken ct = default)
    {
        if (imagePaths is null || imagePaths.Count == 0)
        {
            throw new ArgumentException("Vui lòng chọn ít nhất 1 tệp ảnh.", nameof(imagePaths));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var doc = new PdfDocument();

        foreach (string imgPath in imagePaths)
        {
            if (!File.Exists(imgPath))
            {
                throw new FileNotFoundException("Không tìm thấy tệp ảnh.", imgPath);
            }

            PdfPage page = doc.AddPage();
            using XImage xImg = XImage.FromFile(imgPath);
            if (fitToA4)
            {
                page.Size = PdfSharp.PageSize.A4;
                double maxW = page.Width.Point - 36;
                double maxH = page.Height.Point - 36;
                double scale = Math.Min(maxW / xImg.PointWidth, maxH / xImg.PointHeight);
                double drawW = xImg.PointWidth * scale;
                double drawH = xImg.PointHeight * scale;
                double x = (page.Width.Point - drawW) / 2.0;
                double y = (page.Height.Point - drawH) / 2.0;
                using XGraphics gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(xImg, x, y, drawW, drawH);
            }
            else
            {
                page.Width = XUnit.FromPoint(Math.Max(72, xImg.PointWidth));
                page.Height = XUnit.FromPoint(Math.Max(72, xImg.PointHeight));
                using XGraphics gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(xImg, 0, 0, page.Width.Point, page.Height.Point);
            }
        }

        int pageCount = doc.PageCount;
        doc.Save(outputPath);
        return await RecordOperationAsync("ImagesToPdf", imagePaths, outputPath, pageCount, saveToVault, $"Images={imagePaths.Count}", ct)
            .ConfigureAwait(false);
    }

    public async Task<PdfOperationResult> StampImageOnPdfAsync(
        string inputPdfPath,
        string imagePath,
        string outputPath,
        int pageNumber,
        double xPercent,
        double yPercent,
        double widthPercent,
        bool saveToVault = false,
        CancellationToken ct = default)
    {
        if (!File.Exists(inputPdfPath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp PDF nguồn.", inputPdfPath);
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp ảnh chữ ký / con dấu.", imagePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using PdfDocument inputDoc = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Import);
        using var outputDoc = new PdfDocument();
        for (int i = 0; i < inputDoc.PageCount; i++)
        {
            outputDoc.AddPage(inputDoc.Pages[i]);
        }

        int targetIndex = Math.Clamp(pageNumber - 1, 0, Math.Max(0, outputDoc.PageCount - 1));
        PdfPage targetPage = outputDoc.Pages[targetIndex];
        using XImage stamp = XImage.FromFile(imagePath);

        double drawW = Math.Clamp(widthPercent, 3.0, 80.0) / 100.0 * targetPage.Width.Point;
        double aspect = stamp.PointWidth > 0 ? stamp.PointHeight / stamp.PointWidth : 1.0;
        double drawH = drawW * aspect;
        double drawX = Math.Clamp(xPercent, 0.0, 95.0) / 100.0 * targetPage.Width.Point;
        double drawY = Math.Clamp(yPercent, 0.0, 95.0) / 100.0 * targetPage.Height.Point;

        using (XGraphics gfx = XGraphics.FromPdfPage(targetPage, XGraphicsPdfPageOptions.Append))
        {
            gfx.DrawImage(stamp, drawX, drawY, drawW, drawH);
        }

        int pageCount = outputDoc.PageCount;
        outputDoc.Save(outputPath);
        return await RecordOperationAsync("StampImage", [inputPdfPath, imagePath], outputPath, pageCount, saveToVault, $"Page={targetIndex + 1}", ct)
            .ConfigureAwait(false);
    }

    private string BuildWatermarkText(PdfWatermarkOptions options)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Text))
        {
            parts.Add(options.Text.Trim());
        }

        if (options.IncludeUsername && !string.IsNullOrWhiteSpace(_session.Username))
        {
            parts.Add(_session.Username);
        }

        if (options.IncludeTimestamp)
        {
            parts.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        }

        return parts.Count == 0 ? "CONFIDENTIAL" : string.Join(" • ", parts);
    }

    private static XColor ParseHexColor(string hex, double opacity)
    {
        byte alpha = (byte)Math.Clamp((int)Math.Round(Math.Clamp(opacity, 0.05, 1.0) * 255), 10, 255);
        string clean = (hex ?? "#B00020").Trim().TrimStart('#');
        if (clean.Length == 6 &&
            byte.TryParse(clean[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
            byte.TryParse(clean[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
            byte.TryParse(clean[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return XColor.FromArgb(alpha, r, g, b);
        }

        return XColor.FromArgb(alpha, 176, 0, 32);
    }

    private async Task<PdfOperationResult> RecordOperationAsync(
        string operation,
        IReadOnlyList<string> sources,
        string outputPath,
        int pageCount,
        bool saveToVault,
        string notes,
        CancellationToken ct)
    {
        byte[] outBytes = await File.ReadAllBytesAsync(outputPath, ct).ConfigureAwait(false);
        string shaHex = Convert.ToHexString(SHA256.HashData(outBytes)).ToLowerInvariant();

        string? vaultRel = null;
        if (saveToVault)
        {
            string monthDir = DateTime.UtcNow.ToString("yyyyMM");
            vaultRel = $"Office/{monthDir}/{Path.GetFileNameWithoutExtension(outputPath)}_{shaHex[..8]}.pdf";
            string vaultFull = Path.Combine(_paths.VaultDirectory, vaultRel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(vaultFull)!);
            await File.WriteAllBytesAsync(vaultFull, outBytes, ct).ConfigureAwait(false);
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.OfficeDocumentRecords.Add(new OfficeDocumentRecordEntity
        {
            Id = Guid.NewGuid(),
            Operation = operation,
            SourceFilesJson = JsonSerializer.Serialize(sources.Select(Path.GetFileName)),
            OutputVaultPath = vaultRel ?? outputPath,
            OutputSha256 = shaHex,
            PageCount = pageCount,
            Notes = notes,
            CreatedByUsername = string.IsNullOrWhiteSpace(_session.Username) ? "system" : _session.Username,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(
            $"OFFICE_PDF_{operation.ToUpperInvariant()}",
            ModuleIds.OfficePdfStudio,
            Path.GetFileName(outputPath),
            $"Pages={pageCount};Vault={vaultRel ?? "local"};Sha={shaHex[..12]}",
            "Info",
            ct).ConfigureAwait(false);

        return new PdfOperationResult(outputPath, vaultRel, shaHex, pageCount);
    }
}
