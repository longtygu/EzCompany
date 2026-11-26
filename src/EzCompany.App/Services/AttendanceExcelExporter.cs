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
            "Số công",
            "Tăng ca (h)",
            "Trạng thái",
            "Nguồn",
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
            detailSheet.Cell(r, 7).Value = rec.WorkDayUnits;
            detailSheet.Cell(r, 8).Value = rec.OvertimeHours;
            detailSheet.Cell(r, 9).Value = rec.Status;
            detailSheet.Cell(r, 10).Value = rec.IsManualEntry ? "Thủ công HR" : "App Check-in";
            detailSheet.Cell(r, 11).Value = rec.Notes;
        }
        detailSheet.Columns().AdjustToContents();

        wb.SaveAs(outputPath);
    }

    public void ExportMonthlyPayroll(
        string outputPath,
        int year,
        int month,
        string companyName,
        IReadOnlyList<MonthlyPayrollRow> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        using var wb = new XLWorkbook();
        IXLWorksheet sheet = wb.Worksheets.Add("Bảng lương Tháng");
        sheet.Cell(1, 1).Value = $"BẢNG TÍNH LƯƠNG TỰ ĐỘNG THÁNG {month:D2}/{year} — {companyName}";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        string[] headers =
        [
            "Mã NV",
            "Họ và tên",
            "Phòng ban",
            "Chức danh",
            "Lương cơ bản (VNĐ)",
            "Phụ cấp (VNĐ)",
            "Công chuẩn",
            "Công thực tế",
            "Giờ làm",
            "Tăng ca (h)",
            "Lương theo công",
            "Lương tăng ca",
            "Thưởng / Điều chỉnh",
            "Khấu trừ BHXH",
            "Thực lĩnh (VNĐ)"
        ];

        for (int c = 0; c < headers.Length; c++)
        {
            IXLCell cell = sheet.Cell(3, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            int r = 4 + i;
            MonthlyPayrollRow item = rows[i];
            sheet.Cell(r, 1).Value = item.EmployeeCode;
            sheet.Cell(r, 2).Value = item.EmployeeName;
            sheet.Cell(r, 3).Value = item.Department;
            sheet.Cell(r, 4).Value = item.PositionTitle;
            sheet.Cell(r, 5).Value = item.BaseSalaryVnd;
            sheet.Cell(r, 6).Value = item.AllowanceVnd;
            sheet.Cell(r, 7).Value = item.StandardWorkDays;
            sheet.Cell(r, 8).Value = item.ActualWorkDays;
            sheet.Cell(r, 9).Value = item.TotalWorkedHours;
            sheet.Cell(r, 10).Value = item.OvertimeHours;
            sheet.Cell(r, 11).Value = item.ProratedBaseSalaryVnd;
            sheet.Cell(r, 12).Value = item.OvertimePayVnd;
            sheet.Cell(r, 13).Value = item.BonusOrAdjustmentVnd;
            sheet.Cell(r, 14).Value = item.InsuranceDeductionVnd;
            sheet.Cell(r, 15).Value = item.NetSalaryVnd;

            foreach (int moneyCol in new[] { 5, 6, 11, 12, 13, 14, 15 })
            {
                sheet.Cell(r, moneyCol).Style.NumberFormat.Format = "#,##0";
            }
        }

        int totalRow = 4 + rows.Count;
        sheet.Cell(totalRow, 1).Value = "TỔNG CỘNG";
        sheet.Cell(totalRow, 1).Style.Font.Bold = true;
        foreach (int moneyCol in new[] { 5, 6, 11, 12, 13, 14, 15 })
        {
            decimal sum = moneyCol switch
            {
                5 => rows.Sum(x => x.BaseSalaryVnd),
                6 => rows.Sum(x => x.AllowanceVnd),
                11 => rows.Sum(x => x.ProratedBaseSalaryVnd),
                12 => rows.Sum(x => x.OvertimePayVnd),
                13 => rows.Sum(x => x.BonusOrAdjustmentVnd),
                14 => rows.Sum(x => x.InsuranceDeductionVnd),
                _ => rows.Sum(x => x.NetSalaryVnd)
            };
            IXLCell c = sheet.Cell(totalRow, moneyCol);
            c.Value = sum;
            c.Style.Font.Bold = true;
            c.Style.NumberFormat.Format = "#,##0";
        }

        sheet.Columns().AdjustToContents();
        wb.SaveAs(outputPath);
    }
}
