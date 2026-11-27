using ClosedXML.Excel;
using EzCompany.Core.Data.Entities;

namespace EzCompany.Modules.Legal;

/// <summary>
/// Exports the Legal Contract Registry to a structured Excel workbook.
/// </summary>
public sealed class ContractRegistryExcelExporter
{
    public void ExportContracts(
        string outputPath,
        string companyName,
        IReadOnlyList<ContractRecordEntity> contracts)
    {
        using var wb = new XLWorkbook();
        IXLWorksheet ws = wb.Worksheets.Add("Danh mục Hợp đồng");

        ws.Cell(1, 1).Value = $"{companyName} — SỔ THEO DÕI HỢP ĐỒNG & HẠN HIỆU LỰC";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        string[] headers = ["Số HĐ", "Tên hợp đồng", "Đối tác", "Loại", "Ngày ký", "Hiệu lực từ", "Ngày hết hạn", "Giá trị (VNĐ)", "Trạng thái", "Phụ trách", "Ghi chú"];
        for (int c = 0; c < headers.Length; c++)
        {
            IXLCell cell = ws.Cell(3, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }

        int row = 4;
        foreach (ContractRecordEntity c in contracts)
        {
            ws.Cell(row, 1).Value = c.ContractNumber;
            ws.Cell(row, 2).Value = c.Title;
            ws.Cell(row, 3).Value = c.CounterpartyName;
            ws.Cell(row, 4).Value = c.ContractType;
            ws.Cell(row, 5).Value = c.SignedDateKey;
            ws.Cell(row, 6).Value = c.StartDateKey;
            ws.Cell(row, 7).Value = c.ExpiryDateKey;
            ws.Cell(row, 8).Value = c.ValueVnd;
            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 9).Value = c.Status;
            ws.Cell(row, 10).Value = c.OwnerName;
            ws.Cell(row, 11).Value = c.Notes;
            row++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(outputPath);
    }
}
