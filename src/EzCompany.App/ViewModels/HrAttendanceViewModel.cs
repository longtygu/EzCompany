using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Collaboration;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed partial class HrAttendanceViewModel : ObservableObject
{
    private readonly HrAttendanceService _hrService;
    private readonly AttendanceExcelExporter _excelExporter;
    private readonly LanCollaborationClient _collabClient;
    private readonly ISessionContext _session;

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
    public ObservableCollection<AttendanceRecordEntity> DailyRecords { get; } = [];
    public ObservableCollection<MonthlyAttendanceSummaryRow> MonthlySummary { get; } = [];

    public IReadOnlyList<int> AvailableMonths { get; } = Enumerable.Range(1, 12).ToArray();
    public IReadOnlyList<string> EmploymentStatuses { get; } = ["Active", "OnLeave", "Inactive"];
    public IReadOnlyList<string> AttendanceStatuses { get; } = ["Present", "Late", "HalfDay", "Leave", "Absent"];

    [ObservableProperty] private EmployeeProfileEntity? _selectedEmployee;
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
    [ObservableProperty] private string _editEmploymentStatus = "Active";
    [ObservableProperty] private string _editNotes = string.Empty;

    [ObservableProperty] private int _selectedMonth;
    [ObservableProperty] private int _selectedYear;
    [ObservableProperty] private string _todayCheckStatus = "Hôm nay bạn chưa chấm công.";
    [ObservableProperty] private string _statusBanner = string.Empty;

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IReadOnlyList<EmployeeProfileEntity> list = await _hrService.GetEmployeesAsync();
        Guid? prevId = SelectedEmployee?.Id;
        Employees.Clear();
        foreach (EmployeeProfileEntity e in list)
        {
            Employees.Add(e);
        }

        SelectedEmployee = Employees.FirstOrDefault(e => e.Id == prevId) ?? Employees.FirstOrDefault();

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
    }

    partial void OnSelectedEmployeeChanged(EmployeeProfileEntity? value)
    {
        if (value is null)
        {
            return;
        }

        EditingEmployeeId = value.Id;
        EditEmployeeCode = value.EmployeeCode;
        EditFullName = value.FullName;
        EditDepartment = value.Department;
        EditPositionTitle = value.PositionTitle;
        EditRoleDirector = CompanyRoles.HasRole(value.RolesCsv, CompanyRoles.Director);
        EditRoleAccounting = CompanyRoles.HasRole(value.RolesCsv, CompanyRoles.Accounting);
        EditRoleLegal = CompanyRoles.HasRole(value.RolesCsv, CompanyRoles.Legal);
        EditRoleHr = CompanyRoles.HasRole(value.RolesCsv, CompanyRoles.Hr);
        EditRoleStaff = CompanyRoles.HasRole(value.RolesCsv, CompanyRoles.Staff);
        EditPhone = value.Phone;
        EditEmail = value.Email;
        EditNationalId = value.NationalId;
        EditEmploymentStatus = value.EmploymentStatus;
        EditNotes = value.Notes;
    }

    partial void OnSelectedMonthChanged(int value) => _ = RefreshAllAsync();
    partial void OnSelectedYearChanged(int value) => _ = RefreshAllAsync();

    [RelayCommand]
    private void NewEmployeeForm()
    {
        SelectedEmployee = null;
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
        EditEmploymentStatus = "Active";
        EditNotes = string.Empty;
    }

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

            EmployeeProfileEntity saved = await _hrService.SaveEmployeeAsync(new EmployeeProfileEntity
            {
                Id = EditingEmployeeId,
                EmployeeCode = EditEmployeeCode,
                FullName = EditFullName,
                Department = EditDepartment,
                PositionTitle = EditPositionTitle,
                RolesCsv = CompanyRoles.FormatRoleStorage(roles),
                LinkedMachineId = SelectedEmployee?.LinkedMachineId,
                LinkedUsername = SelectedEmployee?.LinkedUsername,
                Phone = EditPhone,
                Email = EditEmail,
                NationalId = EditNationalId,
                EmploymentStatus = EditEmploymentStatus,
                Notes = EditNotes
            });

            StatusBanner = $"Đã lưu hồ sơ nhân viên {saved.EmployeeCode} — {saved.FullName}.";
            await RefreshAllAsync();
            SelectedEmployee = Employees.FirstOrDefault(e => e.Id == saved.Id);
        }
        catch (Exception ex)
        {
            StatusBanner = $"Lỗi lưu hồ sơ: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteEmployeeAsync()
    {
        if (SelectedEmployee is null)
        {
            return;
        }

        await _hrService.DeleteEmployeeAsync(SelectedEmployee.Id);
        NewEmployeeForm();
        await RefreshAllAsync();
        StatusBanner = "Đã xóa hồ sơ nhân viên.";
    }

    [RelayCommand]
    private async Task SyncFromLanDevicesAsync()
    {
        int added = await _hrService.SyncEmployeesFromPairedDevicesAsync();
        await RefreshAllAsync();
        StatusBanner = $"Đã đồng bộ danh sách máy trạm LAN (thêm mới {added} hồ sơ).";
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
}
