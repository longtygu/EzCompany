using System.IO;
using EzCompany.Core.Data;
using EzCompany.Modules.Accounting;
using EzCompany.Modules.Office;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace EzCompany.App.Services;

/// <summary>
/// Exports individual employee monthly Vietnamese payslip PDF documents completely offline.
/// </summary>
public sealed class PayslipPdfExporter
{
    public void ExportEmployeePayslip(
        string outputPath,
        int year,
        int month,
        string companyName,
        MonthlyPayrollRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        WindowsPdfFontResolver.EnsureInitialized();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var doc = new PdfDocument();
        doc.Info.Title = $"Phieu luong {row.EmployeeCode} - T{month:D2}/{year}";

        PdfPage page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using XGraphics gfx = XGraphics.FromPdfPage(page);

        var titleFont = new XFont("Arial", 16, XFontStyleEx.Bold);
        var sectionFont = new XFont("Arial", 11, XFontStyleEx.Bold);
        var regularFont = new XFont("Arial", 10.5, XFontStyleEx.Regular);
        var boldFont = new XFont("Arial", 10.5, XFontStyleEx.Bold);
        var smallItalic = new XFont("Arial", 9.5, XFontStyleEx.Italic);

        double margin = 48;
        double width = page.Width.Point - margin * 2;
        double y = 48;

        gfx.DrawString(companyName.ToUpperInvariant(), sectionFont, XBrushes.Navy, new XRect(margin, y, width, 20), XStringFormats.TopLeft);
        y += 26;
        gfx.DrawString($"PHIẾU LƯƠNG NHÂN VIÊN — THÁNG {month:D2}/{year}", titleFont, XBrushes.Black, new XRect(margin, y, width, 26), XStringFormats.TopCenter);
        y += 36;

        gfx.DrawLine(new XPen(XColors.DarkGray, 1), margin, y, margin + width, y);
        y += 14;

        DrawInfoPair(gfx, regularFont, boldFont, margin, ref y, width,
            $"Mã nhân viên: {row.EmployeeCode}", $"Họ và tên: {row.EmployeeName}");
        DrawInfoPair(gfx, regularFont, boldFont, margin, ref y, width,
            $"Phòng ban: {(string.IsNullOrWhiteSpace(row.Department) ? "—" : row.Department)}",
            $"Chức danh: {(string.IsNullOrWhiteSpace(row.PositionTitle) ? "—" : row.PositionTitle)}");
        DrawInfoPair(gfx, regularFont, boldFont, margin, ref y, width,
            $"Công chuẩn / Thực tế: {row.StandardWorkDays:F1} / {row.ActualWorkDays:F2} ngày",
            $"Tổng giờ làm / Tăng ca: {row.TotalWorkedHours:F2}h / {row.OvertimeHours:F1}h");
        DrawInfoPair(gfx, regularFont, boldFont, margin, ref y, width,
            $"Số ngày đi muộn: {row.LateDays}", $"Số ngày vắng: {row.AbsentDays}");

        y += 10;
        gfx.DrawString("CHI TIẾT THU NHẬP & KHẤU TRỪ", sectionFont, XBrushes.DarkSlateBlue, new XRect(margin, y, width, 20), XStringFormats.TopLeft);
        y += 22;

        DrawTableRow(gfx, boldFont, margin, ref y, width, "Khoản mục", "Số tiền (VNĐ)", true);
        DrawTableRow(gfx, regularFont, margin, ref y, width, "Lương cơ bản hợp đồng", $"{row.BaseSalaryVnd:N0} đ", false);
        DrawTableRow(gfx, regularFont, margin, ref y, width, $"Lương theo ngày công thực tế ({row.ActualWorkDays:F2}/{row.StandardWorkDays:F1})", $"{row.ProratedBaseSalaryVnd:N0} đ", false);
        DrawTableRow(gfx, regularFont, margin, ref y, width, "Phụ cấp", $"{row.AllowanceVnd:N0} đ", false);
        DrawTableRow(gfx, regularFont, margin, ref y, width, $"Tiền tăng ca ({row.OvertimeHours:F1} giờ x 150%)", $"{row.OvertimePayVnd:N0} đ", false);
        DrawTableRow(gfx, regularFont, margin, ref y, width, "Thưởng / Điều chỉnh khác", $"{row.BonusOrAdjustmentVnd:N0} đ", false);
        DrawTableRow(gfx, regularFont, margin, ref y, width, "Khấu trừ BHXH / BHYT / BHTN", $"-{row.InsuranceDeductionVnd:N0} đ", false);
        DrawTableRow(gfx, boldFont, margin, ref y, width, "THỰC LĨNH (NET)", $"{row.NetSalaryVnd:N0} VNĐ", true);

        y += 12;
        string inWords = VietnameseNumberToWordsConverter.Convert(row.NetSalaryVnd);
        gfx.DrawString($"Bằng chữ: {inWords}", smallItalic, XBrushes.Black, new XRect(margin, y, width, 30), XStringFormats.TopLeft);
        y += 40;

        DrawInfoPair(gfx, boldFont, boldFont, margin, ref y, width, "NGƯỜI NHẬN LƯƠNG", "PHÒNG NHÂN SỰ / GIÁM ĐỐC");
        y += 4;
        DrawInfoPair(gfx, smallItalic, smallItalic, margin, ref y, width, "(Ký và ghi rõ họ tên)", $"(Lập ngày {DateTime.Now:dd/MM/yyyy})");

        doc.Save(outputPath);
    }

    private static void DrawInfoPair(
        XGraphics gfx,
        XFont leftFont,
        XFont rightFont,
        double margin,
        ref double y,
        double totalWidth,
        string leftText,
        string rightText)
    {
        double half = totalWidth / 2.0;
        gfx.DrawString(leftText, leftFont, XBrushes.Black, new XRect(margin, y, half - 8, 18), XStringFormats.TopLeft);
        gfx.DrawString(rightText, rightFont, XBrushes.Black, new XRect(margin + half, y, half, 18), XStringFormats.TopLeft);
        y += 20;
    }

    private static void DrawTableRow(
        XGraphics gfx,
        XFont font,
        double margin,
        ref double y,
        double totalWidth,
        string label,
        string amount,
        bool shaded)
    {
        var rect = new XRect(margin, y, totalWidth, 24);
        if (shaded)
        {
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(230, 238, 248)), rect);
        }

        gfx.DrawRectangle(new XPen(XColors.LightGray, 0.8), rect);
        gfx.DrawString(label, font, XBrushes.Black, new XRect(margin + 8, y + 4, totalWidth * 0.65, 18), XStringFormats.TopLeft);
        gfx.DrawString(amount, font, XBrushes.Black, new XRect(margin + totalWidth * 0.65, y + 4, totalWidth * 0.35 - 8, 18), XStringFormats.TopRight);
        y += 24;
    }
}
