using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Collaboration;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed partial class EmployeeProfileCardItem : ObservableObject
{
    public required EmployeeProfileEntity Entity { get; init; }

    public Guid Id => Entity.Id;
    public string EmployeeCode => Entity.EmployeeCode;
    public string FullName => Entity.FullName;
    public string Department => string.IsNullOrWhiteSpace(Entity.Department) ? "Chưa phân phòng ban" : Entity.Department;
    public string PositionTitle => string.IsNullOrWhiteSpace(Entity.PositionTitle) ? "Chưa cập nhật" : Entity.PositionTitle;
    public string RolesBilingual => CompanyRoles.FormatRolesBilingual(Entity.RolesCsv);
    public string EmploymentStatus => Entity.EmploymentStatus;
    public string Phone => string.IsNullOrWhiteSpace(Entity.Phone) ? "—" : Entity.Phone;
    public string Email => string.IsNullOrWhiteSpace(Entity.Email) ? "—" : Entity.Email;
    public string NationalId => string.IsNullOrWhiteSpace(Entity.NationalId) ? "—" : Entity.NationalId;
    public string Notes => string.IsNullOrWhiteSpace(Entity.Notes) ? "Không có ghi chú" : Entity.Notes;
    public string BaseSalaryText => $"{Entity.BaseSalaryVnd:N0} đ";
    public string AllowanceText => $"{Entity.AllowanceVnd:N0} đ";
    public string InsuranceDeductionText => $"{Entity.InsuranceDeductionVnd:N0} đ";
    public string StandardDaysText => $"{Entity.StandardWorkDaysPerMonth:F1} ngày/tháng";
    public string JoinDateText => Entity.JoinDateUtc.ToLocalTime().ToString("dd/MM/yyyy");

    [ObservableProperty]
    private bool _isExpanded;
}

public sealed partial class EditablePayrollRow : ObservableObject
{
    public required MonthlyPayrollRow Source { get; init; }
    public Action? TotalsChangedCallback { get; init; }

    public Guid EmployeeProfileId => Source.EmployeeProfileId;
    public string EmployeeCode => Source.EmployeeCode;
    public string EmployeeName => Source.EmployeeName;
    public string Department => Source.Department;
    public string PositionTitle => Source.PositionTitle;
    public decimal BaseSalaryVnd => Source.BaseSalaryVnd;
    public decimal AllowanceVnd => Source.AllowanceVnd;
    public double StandardWorkDays => Source.StandardWorkDays;
    public double ActualWorkDays => Source.ActualWorkDays;
    public double TotalWorkedHours => Source.TotalWorkedHours;
    public double OvertimeHours => Source.OvertimeHours;
    public int LateDays => Source.LateDays;
    public int AbsentDays => Source.AbsentDays;
    public decimal ProratedBaseSalaryVnd => Source.ProratedBaseSalaryVnd;
    public decimal OvertimePayVnd => Source.OvertimePayVnd;
    public decimal InsuranceDeductionVnd => Source.InsuranceDeductionVnd;

    [ObservableProperty]
    private decimal _bonusOrAdjustmentVnd;

    [ObservableProperty]
    private decimal _netSalaryVnd;

    partial void OnBonusOrAdjustmentVndChanged(decimal value)
    {
        NetSalaryVnd = Math.Max(0m, ProratedBaseSalaryVnd + AllowanceVnd + OvertimePayVnd + value - InsuranceDeductionVnd);
        TotalsChangedCallback?.Invoke();
    }

    public MonthlyPayrollRow ToRecord() => Source with
    {
        BonusOrAdjustmentVnd = BonusOrAdjustmentVnd,
        NetSalaryVnd = NetSalaryVnd
    };
}

public sealed partial class HrAttendanceViewModel : ObservableObject
{
    private readonly HrAttendanceService _hrService;
    private readonly AttendanceExcelExporter _excelExporter;
    private readonly LanCollaborationClient _collabClient;
    private readonly ISessionContext _session;
    private readonly Dictionary<Guid, decimal> _bonusCache = [];

    public HrAttendanceViewModel(
        HrAttendanceService hrService,
        AttendanceExcelExporter excelExporter,
        LanCollaborationClient collabClient,
        ISessionContext session)
    {
        _hrService = hrService;
        _excelExporter = excelExporter;
        _collabClient = collabClient;
        _session = session;

        SelectedMonth = DateTime.Now.Month;
        SelectedYear = DateTime.Now.Year;
    }

    public ObservableCollection<EmployeeProfileEntity> Employees { get; } = [];
    public ObservableCollection<EmployeeProfileCardItem> EmployeeCards { get; } = [];
    public ObservableCollection<AttendanceRecordEntity> DailyRecords { get; } = [];
    public ObservableCollection<MonthlyAttendanceSummaryRow> MonthlySummary { get; } = [];
    public ObservableCollection<EditablePayrollRow> PayrollRows { get; } = [];

    public IReadOnlyList<int> AvailableMonths { get; } = Enumerable.Range(1, 12).ToArray();
    public IReadOnlyList<string> EmploymentStatuses { get; } = ["Active", "OnLeave", "Inactive"];
    public IReadOnlyList<string> AttendanceStatuses { get; } = ["Present", "Late", "HalfDay", "Leave", "Absent"];

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _employeeSearchQuery = string.Empty;
    [ObservableProperty] private bool _isEmployeeEditorVisible;
    [ObservableProperty] private string _editorModeTitle = "Thêm hồ sơ nhân viên mới";

    // Employee profile editor fields
    [ObservableProperty] private Guid _editingEmployeeId;
    [ObservableProperty] private string _editEmployeeCode = string.Empty;
    [ObservableProperty] private string _editFullName = string.Empty;
    [ObservableProperty] private string _editDepartment = string.Empty;
    [ObservableProperty] private string _editPositionTitle = string.Empty;
    [ObservableProperty] private bool _editRoleDirector;
    [ObservableProperty] private bool _editRoleAccounting;
    [ObservableProperty] private bool _editRoleLegal;
    [ObservableProperty] private bool _editRoleHr;
    [ObservableProperty] private bool _editRoleStaff = true;
    [ObservableProperty] private string _editPhone = string.Empty;
    [ObservableProperty] private string _editEmail = string.Empty;
    [ObservableProperty] private string _editNationalId = string.Empty;
    [ObservableProperty] private DateTime _editJoinDate = DateTime.Today;
    [ObservableProperty] private string _editEmploymentStatus = "Active";
    [ObservableProperty] private decimal _editBaseSalaryVnd = 10_000_000m;
    [ObservableProperty] private decimal _editAllowanceVnd = 1_000_000m;
    [ObservableProperty] private double _editStandardWorkDays = 26.0;
    [ObservableProperty] private decimal _editInsuranceDeductionVnd = 1_050_000m;
    [ObservableProperty] private string _editNotes = string.Empty;

    // Custom hour attendance fields
    [ObservableProperty] private EmployeeProfileEntity? _attendanceTargetEmployee;
    [ObservableProperty] private DateTime _attendanceWorkDate = DateTime.Today;
    [ObservableProperty] private string _attendanceCheckInText = "08:00";
    [ObservableProperty] private string _attendanceCheckOutText = "17:30";
    [ObservableProperty] private string _attendanceStatus = "Present";
    [ObservableProperty] private double _attendanceWorkDayUnits = 1.0;
    [ObservableProperty] private double _attendanceOvertimeHours;
    [ObservableProperty] private string _attendanceNotes = string.Empty;

    // Month / status / payroll summary
    [ObservableProperty] private int _selectedMonth;
    [ObservableProperty] private int _selectedYear;
    [ObservableProperty] private string _todayCheckStatus = "Hôm nay bạn chưa chấm công.";
    [ObservableProperty] private string _payrollSummaryText = string.Empty;
    [ObservableProperty] private string _statusBanner = string.Empty;

    public Func<string, string, bool>? ConfirmDeleteCallback { get; set; }
    public event EventHandler<(int Year, int Month)>? OpenFullAttendanceWindowRequested;

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IReadOnlyList<EmployeeProfileEntity> list = await _hrService.GetEmployeesAsync();
        Guid? prevTargetId = AttendanceTargetEmployee?.Id;

        Employees.Clear();
        foreach (EmployeeProfileEntity e in list)
        {
            Employees.Add(e);
        }

        AttendanceTargetEmployee = Employees.FirstOrDefault(e => e.Id == prevTargetId)
                                ?? Employees.FirstOrDefault(e => string.Equals(e.LinkedMachineId, _session.MachineId, StringComparison.OrdinalIgnoreCase))
                                ?? Employees.FirstOrDefault();

        RebuildFilteredEmployeeCards();

        IReadOnlyList<AttendanceRecordEntity> records = await _hrService.GetAttendanceRecordsAsync(SelectedYear, SelectedMonth);
        DailyRecords.Clear();
        foreach (AttendanceRecordEntity r in records)
        {
            DailyRecords.Add(r);
        }

        IReadOnlyList<MonthlyAttendanceSummaryRow> summary = await _hrService.GetMonthlySummaryAsync(SelectedYear, SelectedMonth);
        MonthlySummary.Clear();
        foreach (MonthlyAttendanceSummaryRow s in summary)
        {
            MonthlySummary.Add(s);
        }

        string todayKey = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        AttendanceRecordEntity? myToday = DailyRecords.FirstOrDefault(r =>
            r.DateKey == todayKey && string.Equals(r.MachineId, _session.MachineId, StringComparison.OrdinalIgnoreCase));
        TodayCheckStatus = myToday is null
            ? "Hôm nay bạn chưa chấm công."
            : $"Hôm nay ({myToday.Status}): Vào {myToday.CheckInUtc?.ToLocalTime():HH:mm} • Ra {myToday.CheckOutUtc?.ToLocalTime().ToString("HH:mm") ?? "Chưa check-out"} • {myToday.WorkedHours:F2}h";

        await RecalculatePayrollAsync();
    }

    partial void OnEmployeeSearchQueryChanged(string value) => RebuildFilteredEmployeeCards();
    partial void OnSelectedMonthChanged(int value) => _ = RefreshAllAsync();
    partial void OnSelectedYearChanged(int value) => _ = RefreshAllAsync();

    partial void OnAttendanceStatusChanged(string value)
    {
        AttendanceWorkDayUnits = value switch
        {
            "HalfDay" => 0.5,
            "Absent" => 0.0,
            _ => 1.0
        };
    }

    private void RebuildFilteredEmployeeCards(Guid? expandEmployeeId = null)
    {
        HashSet<Guid> previouslyExpanded = EmployeeCards
            .Where(c => c.IsExpanded)
            .Select(c => c.Id)
            .ToHashSet();
        if (expandEmployeeId.HasValue)
        {
            previouslyExpanded.Add(expandEmployeeId.Value);
        }

        string q = EmployeeSearchQuery.Trim();
        IEnumerable<EmployeeProfileEntity> filtered = string.IsNullOrEmpty(q)
            ? Employees
            : Employees.Where(e =>
                e.EmployeeCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Department.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.PositionTitle.Contains(q, StringComparison.OrdinalIgnoreCase));

        EmployeeCards.Clear();
        foreach (EmployeeProfileEntity emp in filtered)
        {
            EmployeeCards.Add(new EmployeeProfileCardItem
            {
                Entity = emp,
                IsExpanded = previouslyExpanded.Contains(emp.Id)
            });
        }
    }

    [RelayCommand]
    private void OpenNewEmployeeEditor()
    {
        EditorModeTitle = "Thêm hồ sơ nhân viên mới";
        EditingEmployeeId = Guid.Empty;
        EditEmployeeCode = $"NV-{DateTime.Now:HHmmss}";
        EditFullName = string.Empty;
        EditDepartment = string.Empty;
        EditPositionTitle = string.Empty;
        EditRoleDirector = false;
        EditRoleAccounting = false;
        EditRoleLegal = false;
        EditRoleHr = false;
        EditRoleStaff = true;
        EditPhone = string.Empty;
        EditEmail = string.Empty;
        EditNationalId = string.Empty;
        EditJoinDate = DateTime.Today;
        EditEmploymentStatus = "Active";
        EditBaseSalaryVnd = 10_000_000m;
        EditAllowanceVnd = 1_000_000m;
        EditStandardWorkDays = 26.0;
        EditInsuranceDeductionVnd = 1_050_000m;
        EditNotes = string.Empty;
        IsEmployeeEditorVisible = true;
    }

    [RelayCommand]
    private void EditEmployeeCard(EmployeeProfileCardItem? card)
    {
        if (card is null)
        {
            return;
        }

        EmployeeProfileEntity e = card.Entity;
        EditorModeTitle = $"Chỉnh sửa hồ sơ: {e.EmployeeCode} — {e.FullName}";
        EditingEmployeeId = e.Id;
        EditEmployeeCode = e.EmployeeCode;
        EditFullName = e.FullName;
        EditDepartment = e.Department;
        EditPositionTitle = e.PositionTitle;
        EditRoleDirector = CompanyRoles.HasRole(e.RolesCsv, CompanyRoles.Director);
        EditRoleAccounting = CompanyRoles.HasRole(e.RolesCsv, CompanyRoles.Accounting);
        EditRoleLegal = CompanyRoles.HasRole(e.RolesCsv, CompanyRoles.Legal);
        EditRoleHr = CompanyRoles.HasRole(e.RolesCsv, CompanyRoles.Hr);
        EditRoleStaff = CompanyRoles.HasRole(e.RolesCsv, CompanyRoles.Staff);
        EditPhone = e.Phone;
        EditEmail = e.Email;
        EditNationalId = e.NationalId;
        EditJoinDate = e.JoinDateUtc == default ? DateTime.Today : e.JoinDateUtc.ToLocalTime().Date;
        EditEmploymentStatus = e.EmploymentStatus;
        EditBaseSalaryVnd = e.BaseSalaryVnd;
        EditAllowanceVnd = e.AllowanceVnd;
        EditStandardWorkDays = e.StandardWorkDaysPerMonth <= 0 ? 26.0 : e.StandardWorkDaysPerMonth;
        EditInsuranceDeductionVnd = e.InsuranceDeductionVnd;
        EditNotes = e.Notes;
        IsEmployeeEditorVisible = true;
    }

    [RelayCommand]
    private void CancelEmployeeEditor() => IsEmployeeEditorVisible = false;

    [RelayCommand]
    private async Task SaveEmployeeAsync()
    {
        try
        {
            var roles = new List<string>();
            if (EditRoleDirector) roles.Add(CompanyRoles.Director);
            if (EditRoleAccounting) roles.Add(CompanyRoles.Accounting);
            if (EditRoleLegal) roles.Add(CompanyRoles.Legal);
            if (EditRoleHr) roles.Add(CompanyRoles.Hr);
            if (EditRoleStaff) roles.Add(CompanyRoles.Staff);

            EmployeeProfileEntity? existing = EditingEmployeeId != Guid.Empty
                ? Employees.FirstOrDefault(x => x.Id == EditingEmployeeId)
                : null;

            DateTime joinLocal = EditJoinDate.Date;
            DateTimeOffset joinUtc = new DateTimeOffset(joinLocal, TimeZoneInfo.Local.GetUtcOffset(joinLocal)).ToUniversalTime();

            EmployeeProfileEntity saved = await _hrService.SaveEmployeeAsync(new EmployeeProfileEntity
            {
                Id = EditingEmployeeId,
                EmployeeCode = EditEmployeeCode,
                FullName = EditFullName,
                Department = EditDepartment,
                PositionTitle = EditPositionTitle,
                RolesCsv = CompanyRoles.FormatRoleStorage(roles),
                LinkedMachineId = existing?.LinkedMachineId,
                LinkedUsername = existing?.LinkedUsername,
                Phone = EditPhone,
                Email = EditEmail,
                NationalId = EditNationalId,
                JoinDateUtc = joinUtc,
                EmploymentStatus = EditEmploymentStatus,
                BaseSalaryVnd = EditBaseSalaryVnd,
                AllowanceVnd = EditAllowanceVnd,
                StandardWorkDaysPerMonth = EditStandardWorkDays,
                InsuranceDeductionVnd = EditInsuranceDeductionVnd,
                Notes = EditNotes
            });

            IsEmployeeEditorVisible = false;
            StatusBanner = $"Đã lưu hồ sơ nhân viên {saved.EmployeeCode} — {saved.FullName}.";
            await RefreshAllAsync();
            RebuildFilteredEmployeeCards(saved.Id);
        }
        catch (Exception ex)
        {
            StatusBanner = $"Lỗi lưu hồ sơ: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteEmployeeCardAsync(EmployeeProfileCardItem? card)
    {
        if (card is null)
        {
            return;
        }

        if (ConfirmDeleteCallback is not null &&
            !ConfirmDeleteCallback($"Xóa hồ sơ nhân viên {card.EmployeeCode} — {card.FullName}?", "Xác nhận xóa hồ sơ"))
        {
            return;
        }

        await _hrService.DeleteEmployeeAsync(card.Id);
        if (EditingEmployeeId == card.Id)
        {
            IsEmployeeEditorVisible = false;
        }

        await RefreshAllAsync();
        StatusBanner = $"Đã xóa hồ sơ nhân viên {card.EmployeeCode}.";
    }

    [RelayCommand]
    private void QuickAttendEmployee(EmployeeProfileCardItem? card)
    {
        if (card is null)
        {
            return;
        }

        AttendanceTargetEmployee = Employees.FirstOrDefault(e => e.Id == card.Id) ?? card.Entity;
        AttendanceWorkDate = DateTime.Today;
        SelectedTabIndex = 1;
    }

    [RelayCommand]
    private async Task SyncFromLanDevicesAsync()
    {
        int added = await _hrService.SyncEmployeesFromPairedDevicesAsync();
        await RefreshAllAsync();
        StatusBanner = $"Đã đồng bộ danh sách máy trạm LAN (thêm mới {added} hồ sơ).";
    }

    [RelayCommand]
    private void FillCurrentTimeCheckIn() => AttendanceCheckInText = DateTime.Now.ToString("HH:mm");

    [RelayCommand]
    private void FillCurrentTimeCheckOut() => AttendanceCheckOutText = DateTime.Now.ToString("HH:mm");

    [RelayCommand]
    private async Task SaveCustomAttendanceAsync()
    {
        EmployeeProfileEntity? target = AttendanceTargetEmployee;
        if (target is null)
        {
            target = await _hrService.EnsureCurrentUserProfileAsync();
        }

        if (!TryParseOptionalHourMinute(AttendanceCheckInText, out TimeSpan? inTime))
        {
            StatusBanner = "Giờ VÀO không hợp lệ. Vui lòng nhập theo định dạng HH:mm (ví dụ 08:15) hoặc để trống.";
            return;
        }

        if (!TryParseOptionalHourMinute(AttendanceCheckOutText, out TimeSpan? outTime))
        {
            StatusBanner = "Giờ RA không hợp lệ. Vui lòng nhập theo định dạng HH:mm (ví dụ 17:30) hoặc để trống.";
            return;
        }

        AttendanceRecordEntity rec = await _hrService.SaveCustomAttendanceAsync(
            target.Id,
            DateOnly.FromDateTime(AttendanceWorkDate),
            inTime,
            outTime,
            AttendanceStatus,
            AttendanceWorkDayUnits,
            AttendanceOvertimeHours,
            AttendanceNotes);

        if (!_session.IsDirectorNode)
        {
            _ = await _collabClient.PushAttendanceToDirectorsAsync(rec);
        }

        StatusBanner = $"Đã lưu chấm công ngày {rec.DateKey} cho {rec.EmployeeName}: {rec.WorkedHours:F2}h • {rec.WorkDayUnits:F2} công.";
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task CheckInTodayAsync()
    {
        AttendanceRecordEntity rec = await _hrService.CheckInTodayAsync();
        if (!_session.IsDirectorNode)
        {
            _ = await _collabClient.PushAttendanceToDirectorsAsync(rec);
        }
        StatusBanner = $"Chấm công vào thành công lúc {rec.CheckInUtc?.ToLocalTime():HH:mm:ss} ({rec.Status}).";
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task CheckOutTodayAsync()
    {
        AttendanceRecordEntity rec = await _hrService.CheckOutTodayAsync();
        if (!_session.IsDirectorNode)
        {
            _ = await _collabClient.PushAttendanceToDirectorsAsync(rec);
        }
        StatusBanner = $"Chấm công ra thành công (Tổng giờ: {rec.WorkedHours:F2}h).";
        await RefreshAllAsync();
    }

    [RelayCommand]
    private void OpenFullAttendanceWindow() => OpenFullAttendanceWindowRequested?.Invoke(this, (SelectedYear, SelectedMonth));

    [RelayCommand]
    public async Task RecalculatePayrollAsync()
    {
        foreach (EditablePayrollRow row in PayrollRows)
        {
            _bonusCache[row.EmployeeProfileId] = row.BonusOrAdjustmentVnd;
        }

        IReadOnlyList<MonthlyPayrollRow> calculated = await _hrService.CalculateMonthlyPayrollAsync(
            SelectedYear,
            SelectedMonth,
            _bonusCache);

        PayrollRows.Clear();
        foreach (MonthlyPayrollRow item in calculated)
        {
            PayrollRows.Add(new EditablePayrollRow
            {
                Source = item,
                BonusOrAdjustmentVnd = item.BonusOrAdjustmentVnd,
                NetSalaryVnd = item.NetSalaryVnd,
                TotalsChangedCallback = UpdatePayrollSummaryText
            });
        }

        UpdatePayrollSummaryText();
    }

    private void UpdatePayrollSummaryText()
    {
        decimal totalNet = PayrollRows.Sum(r => r.NetSalaryVnd);
        double totalDays = Math.Round(PayrollRows.Sum(r => r.ActualWorkDays), 2);
        double totalOt = Math.Round(PayrollRows.Sum(r => r.OvertimeHours), 2);
        PayrollSummaryText = $"Tổng {PayrollRows.Count} nhân sự • Tổng công: {totalDays:F2} • Tổng tăng ca: {totalOt:F2}h • Tổng quỹ thực lĩnh: {totalNet:N0} VNĐ";
    }

    [RelayCommand]
    private void ExportMonthlyExcel()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất báo cáo Chấm công Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"ChamCong_{SelectedYear}_{SelectedMonth:D2}.xlsx"
        };

        if (dialog.ShowDialog() == true)
        {
            _excelExporter.ExportMonthlyReport(
                dialog.FileName,
                SelectedYear,
                SelectedMonth,
                _session.CompanyName,
                MonthlySummary.ToArray(),
                DailyRecords.ToArray());

            StatusBanner = $"Đã xuất báo cáo chấm công Excel: {dialog.FileName}";
        }
    }

    [RelayCommand]
    private void ExportPayrollExcel()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất Bảng lương Tự động Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"BangLuong_{SelectedYear}_{SelectedMonth:D2}.xlsx"
        };

        if (dialog.ShowDialog() == true)
        {
            _excelExporter.ExportMonthlyPayroll(
                dialog.FileName,
                SelectedYear,
                SelectedMonth,
                _session.CompanyName,
                PayrollRows.Select(r => r.ToRecord()).ToArray());

            StatusBanner = $"Đã xuất Bảng lương Excel: {dialog.FileName}";
        }
    }

    public static bool TryParseOptionalHourMinute(string? raw, out TimeSpan? result)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            result = null;
            return true;
        }

        string trimmed = raw.Trim();
        if (TimeSpan.TryParseExact(trimmed, [@"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss"], CultureInfo.InvariantCulture, out TimeSpan parsed) &&
            parsed >= TimeSpan.Zero && parsed < TimeSpan.FromDays(1))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }
}
