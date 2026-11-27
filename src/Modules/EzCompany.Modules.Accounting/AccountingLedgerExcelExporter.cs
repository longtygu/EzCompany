using ClosedXML.Excel;
using EzCompany.Core.Data.Entities;

namespace EzCompany.Modules.Accounting;

/// <summary>
/// Exports company Cashbook income/expense entries to a formatted Excel workbook.
/// </summary>
public sealed class AccountingLedgerExcelExporter
{
    public void ExportCashbook(
        string outputPath,
        string companyName,
        IReadOnlyList<CashbookEntryEntity> entries)
    {
        using var wb = new XLWorkbook();
        IXLWorksheet ws = wb.Worksheets.Add("Sổ Quỹ Thu Chi");

        ws.Cell(1, 1).Value = $"{companyName} — SỔ QUỸ THU CHI NỘI BỘ";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        (decimal income, decimal expense, decimal balance) = AccountingLedgerServiceSummary(entries);
        ws.Cell(2, 1).Value = $"Tổng Thu: {income:N0} đ   |   Tổng Chi: {expense:N0} đ   |   Tồn Quỹ: {balance:N0} đ";
        ws.Cell(2, 1).Style.Font.Bold = true;

        string[] headers = ["Ngày", "Loại", "Danh mục", "Số tiền (VNĐ)", "Hình thức", "Số chứng từ", "Diễn giải", "Người lập"];
        for (int c = 0; c < headers.Length; c++)
        {
            IXLCell cell = ws.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }

        int row = 5;
        foreach (CashbookEntryEntity e in entries)
        {
            ws.Cell(row, 1).Value = e.EntryDateKey;
            ws.Cell(row, 2).Value = e.EntryType == "Income" ? "Thu" : "Chi";
            ws.Cell(row, 3).Value = e.Category;
            ws.Cell(row, 4).Value = e.AmountVnd;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 5).Value = e.PaymentMethod == "BankTransfer" ? "Chuyển khoản" : "Tiền mặt";
            ws.Cell(row, 6).Value = e.ReferenceCode;
            ws.Cell(row, 7).Value = e.Description;
            ws.Cell(row, 8).Value = e.CreatedByName;
            row++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(outputPath);
    }

    private static (decimal Income, decimal Expense, decimal Balance) AccountingLedgerServiceSummary(IReadOnlyList<CashbookEntryEntity> entries)
    {
        decimal inc = entries.Where(e => e.EntryType == "Income").Sum(e => e.AmountVnd);
        decimal exp = entries.Where(e => e.EntryType != "Income").Sum(e => e.AmountVnd);
        return (inc, exp, inc - exp);
    }
}
