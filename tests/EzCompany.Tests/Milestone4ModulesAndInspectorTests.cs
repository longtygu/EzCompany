using System.IO;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Database;
using EzCompany.Modules.Accounting;
using EzCompany.Modules.Legal;
using EzCompany.Modules.Office;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using UglyToad.PdfPig;
using Xunit;
using PdfSharpDoc = PdfSharp.Pdf.PdfDocument;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace EzCompany.Tests;

public sealed class Milestone4ModulesAndInspectorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "EzCompanyTests_M4_" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(0, "Không đồng chẵn")]
    [InlineData(15, "Mười lăm đồng chẵn")]
    [InlineData(21, "Hai mươi mốt đồng chẵn")]
    [InlineData(105, "Một trăm linh năm đồng chẵn")]
    [InlineData(1205000, "Một triệu hai trăm linh năm nghìn đồng chẵn")]
    [InlineData(24000, "Hai mươi tư nghìn đồng chẵn")]
    public void VietnameseNumberToWords_ConvertsAccountingAmountsAccurately(decimal amount, string expected)
    {
        Assert.Equal(expected, VietnameseNumberToWordsConverter.Convert(amount));
    }

    [Fact]
    public async Task AccountingModule_ParsesNd70Xml_DetectsDuplicatesAndMathMismatch_AndExportsExcel()
    {
        var paths = new LocalStoragePaths("acct_m4", _tempRoot);
        using ServiceProvider sp = BuildProvider(paths, new AccountingModule());
        await MigrateAsync(sp);

        string xmlDir = Path.Combine(_tempRoot, "xml_fixtures");
        Directory.CreateDirectory(xmlDir);

        string validXml = CreateSampleInvoiceXml("1C26TAA", "0000101", "9900000001", "", "079099001122", 1000000m, 100000m, 1100000m);
        string dupXml = CreateSampleInvoiceXml("1C26TAA", "0000101", "9900000001", "", "079099001122", 1000000m, 100000m, 1100000m);
        string mismatchXml = CreateSampleInvoiceXml("1C26TAA", "0000102", "9900000001", "9900000003", "", 500000m, 50000m, 550500m); // +500đ mismatch (>10đ)

        string f1 = Path.Combine(xmlDir, "inv1.xml");
        string f2 = Path.Combine(xmlDir, "inv2_dup.xml");
        string f3 = Path.Combine(xmlDir, "inv3_mismatch.xml");
        await File.WriteAllTextAsync(f1, validXml, Encoding.UTF8);
        await File.WriteAllTextAsync(f2, dupXml, Encoding.UTF8);
        await File.WriteAllTextAsync(f3, mismatchXml, Encoding.UTF8);

        var service = sp.GetRequiredService<AccountingInvoiceService>();
        IReadOnlyList<AccountingInvoiceEntity> batch1 = await service.ImportBatchAsync([f1, f2, f3]);

        Assert.Equal(3, batch1.Count);
        Assert.False(batch1[0].IsDuplicate);
        Assert.False(batch1[0].HasMathMismatch);
        Assert.Equal("079099001122", batch1[0].BuyerTaxCodeOrCccd);

        Assert.True(batch1[1].IsDuplicate);
        Assert.True(batch1[2].HasMathMismatch);
        Assert.Equal(500m, batch1[2].MathDifference);

        // Re-importing f1 in a second batch must detect DB duplicate
        IReadOnlyList<AccountingInvoiceEntity> batch2 = await service.ImportBatchAsync([f1]);
        Assert.True(Assert.Single(batch2).IsDuplicate);

        // Export Excel and verify readback via ClosedXML
        string xlsxPath = Path.Combine(_tempRoot, "ledger.xlsx");
        var exporter = sp.GetRequiredService<AccountingExcelExporter>();
        await exporter.ExportInvoicesToExcelAsync(batch1, xlsxPath);

        Assert.True(File.Exists(xlsxPath));
        using var wb = new XLWorkbook(xlsxPath);
        IXLWorksheet ws = wb.Worksheet("BangKeHoaDon");
        Assert.Contains("đồng chẵn", ws.Cell(8, 1).GetString());
    }

    [Fact]
    public async Task LegalModule_ExtractsParagraphsAndTables_ComputesDiff_AndExportsHtmlAndDocx()
    {
        var paths = new LocalStoragePaths("legal_m4", _tempRoot);
        using ServiceProvider sp = BuildProvider(paths, new LegalModule());
        await MigrateAsync(sp);

        string origDocx = Path.Combine(_tempRoot, "contract_v1.docx");
        string revDocx = Path.Combine(_tempRoot, "contract_v2.docx");

        CreateSampleContractDocx(origDocx,
            paragraphs: ["Điều 1: Thời hạn thanh toán trong vòng 30 ngày.", "Điều 2: Bảo mật thông tin."],
            tableRow: ["Gói A", "10.000.000"]);

        CreateSampleContractDocx(revDocx,
            paragraphs: ["Điều 1: Thời hạn thanh toán trong vòng 15 ngày.", "Điều 2: Bảo mật thông tin.", "Điều 3: Phạt vi phạm 8%."],
            tableRow: ["Gói A", "12.000.000"]);

        var diffService = sp.GetRequiredService<ContractDiffService>();
        ContractDiffResult diff = diffService.CompareDocxFiles(origDocx, revDocx);

        Assert.True(diff.AddedCount + diff.ModifiedCount >= 2);
        Assert.NotEmpty(diff.SideBySideChangeIndices);
        Assert.Contains(diff.SideBySideLines, l => l.NewText.Contains("12.000.000"));

        var exporter = sp.GetRequiredService<ContractReportExporter>();
        string htmlPath = Path.Combine(_tempRoot, "diff_report.html");
        string docxReportPath = Path.Combine(_tempRoot, "diff_report.docx");

        await exporter.ExportHtmlReportAsync(htmlPath, "HĐ Mẫu", "v1.docx", "v2.docx", diff, "Cần chỉnh điều khoản thanh toán");
        await exporter.ExportDocxReportAsync(docxReportPath, "HĐ Mẫu", "v1.docx", "v2.docx", diff, "Cần chỉnh điều khoản thanh toán");
        LegalContractReviewEntity saved = await exporter.SaveToVaultAndDatabaseAsync("HĐ Mẫu", origDocx, revDocx, diff, "Cần chỉnh điều khoản thanh toán", htmlPath);

        Assert.True(File.Exists(htmlPath));
        Assert.True(File.Exists(docxReportPath));
        Assert.Contains("12.000.000", await File.ReadAllTextAsync(htmlPath));
        Assert.Equal(64, saved.OriginalSha256.Length);
        Assert.True(File.Exists(Path.Combine(paths.VaultDirectory, saved.OriginalVaultPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task OfficePdfStudio_MergeSplitRotateWatermark_WorkOfflineAndVerifyWithPdfPig()
    {
        var paths = new LocalStoragePaths("office_m4", _tempRoot);
        using ServiceProvider sp = BuildProvider(paths, new OfficeModule());
        await MigrateAsync(sp);

        WindowsPdfFontResolver.EnsureInitialized();
        string pdf1 = Path.Combine(_tempRoot, "doc1.pdf");
        string pdf2 = Path.Combine(_tempRoot, "doc2.pdf");
        CreateSamplePdf(pdf1, ["Trang 1 Tai lieu A", "Trang 2 Tai lieu A"]);
        CreateSamplePdf(pdf2, ["Trang 3 Tai lieu B"]);

        var studio = sp.GetRequiredService<LocalPdfStudioService>();

        // Merge (2 + 1 = 3 pages)
        string merged = Path.Combine(_tempRoot, "merged.pdf");
        PdfOperationResult mergeRes = await studio.MergePdfsAsync([pdf1, pdf2], merged, saveToVault: true);
        Assert.Equal(3, mergeRes.PageCount);
        Assert.NotNull(mergeRes.VaultRelativePath);
        Assert.Equal(3, studio.InspectPdf(merged).PageCount);

        // Split ("1, 3" -> 2 pages)
        string split = Path.Combine(_tempRoot, "split.pdf");
        PdfOperationResult splitRes = await studio.SplitPdfAsync(merged, "1, 3", split);
        Assert.Equal(2, splitRes.PageCount);
        Assert.Contains("Trang 3 Tai lieu B", studio.InspectPdf(split).TextSample);

        // Rotate (page 2 by 90 deg)
        string rotated = Path.Combine(_tempRoot, "rotated.pdf");
        _ = await studio.RotatePdfAsync(merged, 90, "2", rotated);
        using (PdfPigDoc pig = PdfPigDoc.Open(rotated))
        {
            Assert.Equal(0, pig.GetPage(1).Rotation.Value);
            Assert.Equal(90, pig.GetPage(2).Rotation.Value);
        }

        // Watermark diagonal Unicode
        string watermarked = Path.Combine(_tempRoot, "watermarked.pdf");
        _ = await studio.WatermarkPdfAsync(
            merged,
            new PdfWatermarkOptions("NOI BO BAO MAT", IncludeTimestamp: false, IncludeUsername: false, Opacity: 0.35, FontSize: 28),
            watermarked,
            saveToVault: true);

        PdfInspectionResult wmInfo = studio.InspectPdf(watermarked);
        Assert.Equal(3, wmInfo.PageCount);
        Assert.Contains("NOI BO BAO MAT", wmInfo.TextSample);
    }

    [Fact]
    public async Task ReadOnlyDbInspector_ReadsEmployeeSnapshotWithoutMutatingFileOrMixingDirectorDb()
    {
        var empPaths = new LocalStoragePaths("emp_inspect", _tempRoot);
        using ServiceProvider empSp = BuildProvider(empPaths, new AccountingModule());
        await MigrateAsync(empSp);

        string xmlPath = Path.Combine(_tempRoot, "emp_inv.xml");
        await File.WriteAllTextAsync(
            xmlPath,
            CreateSampleInvoiceXml("1C26TBB", "0000555", "9900000011", "9900000022", "", 200000m, 20000m, 220000m));
        await empSp.GetRequiredService<AccountingInvoiceService>().ImportBatchAsync([xmlPath]);

        var snapSvc = new DatabaseSnapshotService(
            empSp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            empPaths);
        DatabaseSnapshotArtifact artifact = await snapSvc.CreateConsistentSnapshotAsync();

        string copiedDb = Path.Combine(_tempRoot, "director_inspected_copy.db");
        File.Copy(artifact.TempFilePath, copiedDb);
        DatabaseSnapshotService.SafeDeleteTempFile(artifact.TempFilePath);

        string shaBefore = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(copiedDb))).ToLowerInvariant();

        // Director node has its own separate app.db (with 0 invoices)
        var dirPaths = new LocalStoragePaths("dir_inspect", _tempRoot);
        using ServiceProvider dirSp = BuildProvider(dirPaths);
        await MigrateAsync(dirSp);

        var inspector = new ReadOnlyDbInspectorService();
        InspectedDatabaseSnapshot inspected = await inspector.InspectAsync(copiedDb);

        Assert.Single(inspected.Invoices);
        Assert.Equal("0000555", inspected.Invoices[0].InvoiceNumber);

        // Inspected snapshot file must remain byte-for-byte identical
        string shaAfter = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(copiedDb))).ToLowerInvariant();
        Assert.Equal(shaBefore, shaAfter);

        // Director's own app.db must remain untouched (0 invoices)
        await using AppDbContext dirDb = await dirSp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(0, await dirDb.AccountingInvoices.CountAsync());
    }

    private static ServiceProvider BuildProvider(ILocalStoragePaths paths, params IAppModule[] modules)
    {
        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddEzCrypto();
        services.AddEzData(paths);
        foreach (IAppModule mod in modules)
        {
            services.AddSingleton<IAppModule>(mod);
            mod.RegisterServices(services);
        }

        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(IServiceProvider sp)
    {
        await using AppDbContext db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    private static string CreateSampleInvoiceXml(
        string series,
        string number,
        string sellerMst,
        string buyerMst,
        string buyerCccd,
        decimal subTotal,
        decimal vat,
        decimal total) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <HDon>
          <DLHDon>
            <TTChung>
              <KHMSHDon>1</KHMSHDon>
              <KHHDon>{series[1..]}</KHHDon>
              <SHDon>{number}</SHDon>
              <NLap>2026-11-20</NLap>
            </TTChung>
            <NDHDon>
              <NBan>
                <Ten>Cong ty Ban Mau</Ten>
                <MST>{sellerMst}</MST>
              </NBan>
              <NMua>
                <Ten>Khach hang Mau</Ten>
                <MST>{buyerMst}</MST>
                <CCCDan>{buyerCccd}</CCCDan>
              </NMua>
              <DSHHDVu>
                <HHDVu>
                  <THHDVu>Dich vu tu van</THHDVu>
                  <SLuong>1</SLuong>
                  <DGia>{subTotal}</DGia>
                  <ThTien>{subTotal}</ThTien>
                </HHDVu>
              </DSHHDVu>
              <TToan>
                <TgTCThue>{subTotal}</TgTCThue>
                <TgTThue>{vat}</TgTThue>
                <TgTTTBSo>{total}</TgTTTBSo>
              </TToan>
            </NDHDon>
          </DLHDon>
        </HDon>
        """;

    private static void CreateSampleContractDocx(string path, string[] paragraphs, string[] tableRow)
    {
        using WordprocessingDocument doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        MainDocumentPart main = doc.AddMainDocumentPart();
        main.Document = new Document();
        Body body = main.Document.AppendChild(new Body());

        foreach (string text in paragraphs)
        {
            body.AppendChild(new Paragraph(new Run(new Text(text))));
        }

        var table = new Table();
        var tr = new TableRow();
        foreach (string cellText in tableRow)
        {
            tr.AppendChild(new TableCell(new Paragraph(new Run(new Text(cellText)))));
        }
        table.AppendChild(tr);
        body.AppendChild(table);

        main.Document.Save();
    }

    private static void CreateSamplePdf(string path, string[] pageTexts)
    {
        using var doc = new PdfSharpDoc();
        var font = new XFont("Arial", 14, XFontStyleEx.Regular);
        foreach (string txt in pageTexts)
        {
            PdfPage page = doc.AddPage();
            using XGraphics gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString(txt, font, XBrushes.Black, new XPoint(50, 80));
        }

        doc.Save(path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
