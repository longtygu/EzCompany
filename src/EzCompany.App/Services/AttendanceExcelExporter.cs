using ClosedXML.Excel;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;

namespace EzCompany.App.Services;

/// <summary>
/// Exports monthly employee attendance summary and detailed daily check-in/out logs to <c>.xlsx</c>.
/// </summary>
public sealed class AttendanceExcelExporter
{
    public void ExportMonthlyReport(
        string outputPath,
        int year,
        int month,
        string companyName,
        IReadOnlyList<MonthlyAttendanceSummaryRow> summaries,
        IReadOnlyList<AttendanceRecordEntity> dailyRecords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        using var wb = new XLWorkbook();

        IXLWorksheet sumSheet = wb.Worksheets.Add("Tổng hợp Chấm công");
        sumSheet.Cell(1, 1).Value = $"BÁO CÁO CHẤM CÔNG THÁNG {month:D2}/{year} — {companyName}";
        sumSheet.Cell(1, 1).Style.Font.Bold = true;
        sumSheet.Cell(1, 1).Style.Font.FontSize = 14;

        string[] sumHeaders =
        [
            "Mã NV",
            "Họ và tên",
            "Đúng giờ (ngày)",
            "Đi muộn (ngày)",
            "Nửa ngày",
            "Nghỉ phép",
            "Vắng mặt",
            "Tổng giờ làm"
        ];
        for (int c = 0; c < sumHeaders.Length; c++)
        {
            IXLCell cell = sumSheet.Cell(3, c + 1);
            cell.Value = sumHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }

        for (int i = 0; i < summaries.Count; i++)
        {
            int r = 4 + i;
            MonthlyAttendanceSummaryRow item = summaries[i];
            sumSheet.Cell(r, 1).Value = item.EmployeeCode;
            sumSheet.Cell(r, 2).Value = item.EmployeeName;
            sumSheet.Cell(r, 3).Value = item.PresentDays;
            sumSheet.Cell(r, 4).Value = item.LateDays;
            sumSheet.Cell(r, 5).Value = item.HalfDays;
            sumSheet.Cell(r, 6).Value = item.LeaveDays;
            sumSheet.Cell(r, 7).Value = item.AbsentDays;
            sumSheet.Cell(r, 8).Value = item.TotalWorkedHours;
        }
        sumSheet.Columns().AdjustToContents();

        IXLWorksheet detailSheet = wb.Worksheets.Add("Chi tiết Chấm công");
        string[] detailHeaders =
        [
            "Ngày",
            "Mã NV",
            "Họ và tên",
            "Giờ vào (Local)",
            "Giờ ra (Local)",
            "Số giờ làm",
            "Trạng thái",
            "Ghi chú"
        ];
        for (int c = 0; c < detailHeaders.Length; c++)
        {
            IXLCell cell = detailSheet.Cell(1, c + 1);
            cell.Value = detailHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }

        for (int i = 0; i < dailyRecords.Count; i++)
        {
            int r = 2 + i;
            AttendanceRecordEntity rec = dailyRecords[i];
            detailSheet.Cell(r, 1).Value = rec.DateKey;
            detailSheet.Cell(r, 2).Value = rec.EmployeeCode;
            detailSheet.Cell(r, 3).Value = rec.EmployeeName;
            detailSheet.Cell(r, 4).Value = rec.CheckInUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "-";
            detailSheet.Cell(r, 5).Value = rec.CheckOutUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "-";
            detailSheet.Cell(r, 6).Value = rec.WorkedHours;
            detailSheet.Cell(r, 7).Value = rec.Status;
            detailSheet.Cell(r, 8).Value = rec.Notes;
        }
        detailSheet.Columns().AdjustToContents();

        wb.SaveAs(outputPath);
    }
}
