using System.IO;
using ClosedXML.Excel;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;

namespace EzCompany.Modules.Accounting;

public sealed class AccountingExcelExporter
{
    private readonly IAuditLogger _audit;

    public AccountingExcelExporter(IAuditLogger audit)
    {
        _audit = audit;
    }

    public async Task ExportInvoicesToExcelAsync(
        IReadOnlyList<AccountingInvoiceEntity> invoices,
        string outputXlsxPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputXlsxPath))!);

        using var workbook = new XLWorkbook();
        IXLWorksheet ws = workbook.Worksheets.Add("BangKeHoaDon");

        ws.Cell(1, 1).Value = "BẢNG KÊ HÓA ĐƠN ĐIỆN TỬ ĐẦU VÀO / ĐẦU RA";
        ws.Range(1, 1, 1, 12).Merge().Style.Font.SetBold().Font.SetFontSize(14);

        string[] headers =
        [
            "STT", "Ký hiệu", "Số HĐ", "Ngày lập", "MST Người bán", "Tên Người bán",
            "MST / CCCD Mua", "Tên Người mua", "Tiền hàng (VND)", "Thuế VAT (VND)",
            "Tổng cộng (VND)", "Kiểm tra & Cảnh báo"
        ];

        for (int col = 0; col < headers.Length; col++)
        {
            IXLCell cell = ws.Cell(3, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.SetBold();
            cell.Style.Fill.SetBackgroundColor(XLColor.LightSteelBlue);
        }

        int row = 4;
        for (int i = 0; i < invoices.Count; i++)
        {
            AccountingInvoiceEntity inv = invoices[i];
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = inv.InvoiceSeries;
            ws.Cell(row, 3).Value = inv.InvoiceNumber;
            ws.Cell(row, 4).Value = inv.IssueDate.ToLocalTime().ToString("dd/MM/yyyy");
            ws.Cell(row, 5).Value = inv.SellerTaxCode;
            ws.Cell(row, 6).Value = inv.SellerName;
            ws.Cell(row, 7).Value = inv.BuyerTaxCodeOrCccd;
            ws.Cell(row, 8).Value = inv.BuyerName;
            ws.Cell(row, 9).Value = inv.SubTotal;
            ws.Cell(row, 10).Value = inv.VatAmount;
            ws.Cell(row, 11).Value = inv.TotalAmount;
            ws.Cell(row, 12).Value = inv.ValidationNotes;

            ws.Range(row, 9, row, 11).Style.NumberFormat.Format = "#,##0";
            if (inv.IsDuplicate || inv.HasMathMismatch)
            {
                ws.Row(row).Style.Fill.SetBackgroundColor(XLColor.LightPink);
            }

            row++;
        }

        decimal totalSub = invoices.Sum(x => x.SubTotal);
        decimal totalVat = invoices.Sum(x => x.VatAmount);
        decimal grandTotal = invoices.Sum(x => x.TotalAmount);

        ws.Cell(row, 8).Value = "TỔNG CỘNG:";
        ws.Cell(row, 8).Style.Font.SetBold();
        ws.Cell(row, 9).Value = totalSub;
        ws.Cell(row, 10).Value = totalVat;
        ws.Cell(row, 11).Value = grandTotal;
        ws.Range(row, 9, row, 11).Style.Font.SetBold().NumberFormat.Format = "#,##0";

        int wordsRow = row + 1;
        ws.Cell(wordsRow, 1).Value = $"Bằng chữ: {VietnameseNumberToWordsConverter.Convert(grandTotal)}";
        ws.Range(wordsRow, 1, wordsRow, 12).Merge().Style.Font.SetItalic().Font.SetBold();

        ws.Columns().AdjustToContents();
        workbook.SaveAs(outputXlsxPath);

        await _audit.LogAsync(
            "ACCOUNTING_EXPORT_EXCEL",
            ModuleIds.AccountingInvoiceXml,
            Path.GetFileName(outputXlsxPath),
            $"RowCount={invoices.Count};GrandTotal={grandTotal}",
            "Info",
            ct).ConfigureAwait(false);
    }
}
