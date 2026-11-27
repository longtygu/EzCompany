using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Collaboration;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed record EmployeeFilterOption(Guid? EmployeeId, string DisplayName);

public sealed class AttendanceLogDisplayRow
{
    public required AttendanceRecordEntity Entity { get; init; }
    public Guid Id => Entity.Id;
    public string DateKey => Entity.DateKey;
    public string EmployeeCode => Entity.EmployeeCode;
    public string EmployeeName => Entity.EmployeeName;
    public string CheckInLocal => Entity.CheckInUtc?.ToLocalTime().ToString("HH:mm") ?? "—";
    public string CheckOutLocal => Entity.CheckOutUtc?.ToLocalTime().ToString("HH:mm") ?? "—";
    public double WorkedHours => Entity.WorkedHours;
    public double WorkDayUnits => Entity.WorkDayUnits;
    public double OvertimeHours => Entity.OvertimeHours;
    public string Status => Entity.Status;
    public string SourceText => Entity.IsManualEntry ? "Thủ công HR" : "App Check-in";
    public string Notes => Entity.Notes;
}

public sealed partial class AttendanceDetailViewModel : ObservableObject
{
    private readonly HrAttendanceService _hrService;
    private readonly AttendanceExcelExporter _excelExporter;
    private readonly LanCollaborationClient _collabClient;
    private readonly ISessionContext _session;
    private bool _suppressAutoReload;
    private bool _suppressEditorAutoLoad;

    public AttendanceDetailViewModel(
        HrAttendanceService hrService,
        AttendanceExcelExporter excelExporter,
        LanCollaborationClient collabClient,
        ISessionContext session)
    {
        _hrService = hrService;
        _excelExporter = excelExporter;
        _collabClient = collabClient;
        _session = session;

        _suppressAutoReload = true;
        SelectedMonth = DateTime.Now.Month;
        SelectedYear = DateTime.Now.Year;
        _suppressAutoReload = false;
    }

    public IReadOnlyList<int> AvailableMonths { get; } = Enumerable.Range(1, 12).ToArray();
    public IReadOnlyList<string> AttendanceStatuses { get; } = ["Present", "Late", "HalfDay", "Leave", "Absent"];

    public ObservableCollection<EmployeeProfileEntity> Employees { get; } = [];
    public ObservableCollection<EmployeeFilterOption> EmployeeFilterOptions { get; } = [];
    public ObservableCollection<AttendanceLogDisplayRow> DailyRecords { get; } = [];

    [ObservableProperty] private int _selectedMonth;
    [ObservableProperty] private int _selectedYear;
    [ObservableProperty] private EmployeeFilterOption? _selectedEmployeeFilter;
    [ObservableProperty] private DataView? _dailyMatrixView;
    [ObservableProperty] private AttendanceLogDisplayRow? _selectedRecord;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private string _statusBanner = string.Empty;

    // Inline editor inside detail window
    [ObservableProperty] private EmployeeProfileEntity? _editEmployee;
    [ObservableProperty] private DateTime _editWorkDate = DateTime.Today;
    [ObservableProperty] private string _editCheckInText = "08:00";
    [ObservableProperty] private string _editCheckOutText = "17:30";
    [ObservableProperty] private string _editStatus = "Present";
    [ObservableProperty] private double _editWorkDayUnits = 1.0;
    [ObservableProperty] private double _editOvertimeHours;
    [ObservableProperty] private string _editNotes = string.Empty;
    [ObservableProperty] private string _editorHeaderText = "Bấm một ô ngày trong Bảng ma trận hoặc một dòng nhật ký để sửa chấm công.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool _editingExistingRecord;

    public string SaveButtonText => EditingExistingRecord ? "Cập nhật chấm công ngày này" : "Lưu chấm công mới";
    public Func<string, string, bool>? ConfirmCallback { get; set; }

    public event EventHandler? DataModified;

    partial void OnSelectedMonthChanged(int value)
    {
        if (!_suppressAutoReload) _ = RefreshAsync();
    }

    partial void OnSelectedYearChanged(int value)
    {
        if (!_suppressAutoReload) _ = RefreshAsync();
    }

    partial void OnSelectedEmployeeFilterChanged(EmployeeFilterOption? value)
    {
        if (!_suppressAutoReload) _ = RefreshAsync();
    }

    partial void OnEditStatusChanged(string value)
    {
        EditWorkDayUnits = value switch
        {
            "HalfDay" => 0.5,
            "Absent" => 0.0,
            _ => 1.0
        };
    }

    partial void OnEditEmployeeChanged(EmployeeProfileEntity? value)
    {
        if (!_suppressEditorAutoLoad && !_suppressAutoReload)
        {
            _ = LoadEditorForCurrentSelectionAsync();
        }
    }

    partial void OnEditWorkDateChanged(DateTime value)
    {
        if (!_suppressEditorAutoLoad && !_suppressAutoReload)
        {
            _ = LoadEditorForCurrentSelectionAsync();
        }
    }

    partial void OnSelectedRecordChanged(AttendanceLogDisplayRow? value)
    {
        if (value is null)
        {
            return;
        }

        ApplyRecordToEditor(value.Entity);
    }

    public async Task SelectMatrixCellAsync(string employeeCode, int day)
    {
        EmployeeProfileEntity? emp = Employees.FirstOrDefault(e =>
            string.Equals(e.EmployeeCode, employeeCode, StringComparison.OrdinalIgnoreCase));
        if (emp is null)
        {
            return;
        }

        int year = Math.Clamp(SelectedYear, 2000, 2100);
        int month = Math.Clamp(SelectedMonth, 1, 12);
        int maxDay = DateTime.DaysInMonth(year, month);
        if (day < 1 || day > maxDay)
        {
            return;
        }

        _suppressEditorAutoLoad = true;
        try
        {
            EditEmployee = emp;
            EditWorkDate = new DateTime(year, month, day);
        }
        finally
        {
            _suppressEditorAutoLoad = false;
        }

        await LoadEditorForCurrentSelectionAsync();
    }

    public async Task LoadEditorForCurrentSelectionAsync()
    {
        if (EditEmployee is null)
        {
            return;
        }

        DateOnly workDate = DateOnly.FromDateTime(EditWorkDate);
        string dateKey = workDate.ToString("yyyy-MM-dd");
        AttendanceRecordEntity? rec = DailyRecords
            .Select(r => r.Entity)
            .FirstOrDefault(r => r.EmployeeProfileId == EditEmployee.Id && r.DateKey == dateKey)
            ?? await _hrService.GetAttendanceRecordForDayAsync(EditEmployee.Id, workDate);

        if (rec is not null)
        {
            ApplyRecordToEditor(rec);
        }
        else
        {
            EditingExistingRecord = false;
            EditCheckInText = "08:00";
            EditCheckOutText = "17:30";
            EditStatus = "Present";
            EditWorkDayUnits = 1.0;
            EditOvertimeHours = 0;
            EditNotes = string.Empty;
            EditorHeaderText = $"➕ Ngày {workDate:dd/MM/yyyy} — {EditEmployee.FullName}: CHƯA có chấm công → nhập giờ rồi bấm Lưu để tạo mới";
        }
    }

    private void ApplyRecordToEditor(AttendanceRecordEntity e)
    {
        _suppressEditorAutoLoad = true;
        try
        {
            EditEmployee = Employees.FirstOrDefault(x => x.Id == e.EmployeeProfileId) ?? EditEmployee;
            if (DateOnly.TryParse(e.DateKey, out DateOnly d))
            {
                EditWorkDate = d.ToDateTime(TimeOnly.MinValue);
            }
        }
        finally
        {
            _suppressEditorAutoLoad = false;
        }

        EditCheckInText = e.CheckInUtc?.ToLocalTime().ToString("HH:mm") ?? string.Empty;
        EditCheckOutText = e.CheckOutUtc?.ToLocalTime().ToString("HH:mm") ?? string.Empty;
        EditStatus = e.Status;
        EditWorkDayUnits = e.WorkDayUnits;
        EditOvertimeHours = e.OvertimeHours;
        EditNotes = e.Notes;
        EditingExistingRecord = true;

        string inTxt = string.IsNullOrEmpty(EditCheckInText) ? "—" : EditCheckInText;
        string outTxt = string.IsNullOrEmpty(EditCheckOutText) ? "—" : EditCheckOutText;
        string src = e.IsManualEntry ? "Thủ công HR" : "App Check-in";
        string dateDisplay = DateOnly.TryParse(e.DateKey, out DateOnly parsedDate) ? parsedDate.ToString("dd/MM/yyyy") : e.DateKey;
        EditorHeaderText = $"✏ Đang sửa chấm công ĐÃ CÓ ngày {dateDisplay} — {e.EmployeeName} • Hiện tại: Vào {inTxt} • Ra {outTxt} • {e.WorkedHours:F2}h • {e.Status} • Nguồn: {src}";
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _suppressAutoReload = true;
        try
        {
            IReadOnlyList<EmployeeProfileEntity> emps = await _hrService.GetEmployeesAsync();
            Guid? prevFilterEmp = SelectedEmployeeFilter?.EmployeeId;
            Guid? prevEditEmp = EditEmployee?.Id;

            Employees.Clear();
            EmployeeFilterOptions.Clear();
            EmployeeFilterOptions.Add(new EmployeeFilterOption(null, "Tất cả nhân viên"));
            foreach (EmployeeProfileEntity emp in emps)
            {
                Employees.Add(emp);
                EmployeeFilterOptions.Add(new EmployeeFilterOption(emp.Id, $"{emp.EmployeeCode} — {emp.FullName}"));
            }

            SelectedEmployeeFilter = EmployeeFilterOptions.FirstOrDefault(o => o.EmployeeId == prevFilterEmp)
                                  ?? EmployeeFilterOptions.FirstOrDefault();
            EditEmployee = Employees.FirstOrDefault(e => e.Id == prevEditEmp) ?? Employees.FirstOrDefault();

            int year = Math.Clamp(SelectedYear, 2000, 2100);
            int month = Math.Clamp(SelectedMonth, 1, 12);
            Guid? filterEmpId = SelectedEmployeeFilter?.EmployeeId;

            IReadOnlyList<AttendanceRecordEntity> records = await _hrService.GetAttendanceRecordsAsync(year, month, filterEmpId);
            DailyRecords.Clear();
            foreach (AttendanceRecordEntity rec in records)
            {
                DailyRecords.Add(new AttendanceLogDisplayRow { Entity = rec });
            }

            BuildDailyMatrix(year, month, filterEmpId.HasValue ? emps.Where(x => x.Id == filterEmpId.Value).ToArray() : emps, records);
            SummaryText = $"Tháng {month:D2}/{year}: {records.Count} lượt chấm công • Tổng công: {records.Sum(r => r.WorkDayUnits):F2} • Tổng giờ: {records.Sum(r => r.WorkedHours):F2}h";
        }
        finally
        {
            _suppressAutoReload = false;
        }
    }

    private void BuildDailyMatrix(
        int year,
        int month,
        IReadOnlyList<EmployeeProfileEntity> employees,
        IReadOnlyList<AttendanceRecordEntity> records)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        var table = new DataTable();
        table.Columns.Add("Mã NV", typeof(string));
        table.Columns.Add("Họ tên", typeof(string));

        for (int day = 1; day <= daysInMonth; day++)
        {
            table.Columns.Add(day.ToString("D2"), typeof(string));
        }

        table.Columns.Add("Tổng công", typeof(string));
        table.Columns.Add("Tổng giờ", typeof(string));
        table.Columns.Add("Tăng ca (h)", typeof(string));
        table.Columns.Add("Đi muộn", typeof(int));

        ILookup<Guid, AttendanceRecordEntity> byEmp = records.ToLookup(r => r.EmployeeProfileId);

        foreach (EmployeeProfileEntity emp in employees)
        {
            DataRow row = table.NewRow();
            row["Mã NV"] = emp.EmployeeCode;
            row["Họ tên"] = emp.FullName;

            Dictionary<string, AttendanceRecordEntity> byDate = byEmp[emp.Id]
                .GroupBy(r => r.DateKey)
                .ToDictionary(g => g.Key, g => g.First());

            double totalUnits = 0;
            double totalHours = 0;
            double totalOt = 0;
            int lateCount = 0;

            for (int day = 1; day <= daysInMonth; day++)
            {
                string key = $"{year:D4}-{month:D2}-{day:D2}";
                if (byDate.TryGetValue(key, out AttendanceRecordEntity? rec))
                {
                    row[day.ToString("D2")] = FormatMatrixCell(rec);
                    totalUnits += rec.WorkDayUnits;
                    totalHours += rec.WorkedHours;
                    totalOt += rec.OvertimeHours;
                    if (rec.Status == "Late")
                    {
                        lateCount++;
                    }
                }
                else
                {
                    row[day.ToString("D2")] = FormatNonWorkingDayHint(year, month, day);
                }
            }

            row["Tổng công"] = totalUnits.ToString("F2");
            row["Tổng giờ"] = totalHours.ToString("F2");
            row["Tăng ca (h)"] = totalOt.ToString("F1");
            row["Đi muộn"] = lateCount;
            table.Rows.Add(row);
        }

        DailyMatrixView = table.DefaultView;
    }

    private static string FormatNonWorkingDayHint(int year, int month, int day)
    {
        if ((month == 1 && day == 1) ||
            (month == 4 && day == 30) ||
            (month == 5 && day == 1) ||
            (month == 9 && day == 2))
        {
            return "Lễ";
        }

        var date = new DateTime(year, month, day);
        return date.DayOfWeek switch
        {
            DayOfWeek.Sunday => "CN",
            DayOfWeek.Saturday => "T7",
            _ => string.Empty
        };
    }

    private static string FormatMatrixCell(AttendanceRecordEntity rec)
    {
        if (rec.Status is "Absent" or "Leave")
        {
            return rec.Status == "Absent" ? "Vắng" : "Nghỉ phép";
        }

        string inTxt = rec.CheckInUtc?.ToLocalTime().ToString("HH:mm") ?? "?";
        string outTxt = rec.CheckOutUtc?.ToLocalTime().ToString("HH:mm") ?? "?";
        string tag = rec.Status == "Late" ? " (Muộn)" : rec.Status == "HalfDay" ? " (0.5)" : string.Empty;
        return $"{inTxt}-{outTxt}{tag}";
    }

    [RelayCommand]
    private async Task SaveManualEditAsync()
    {
        if (EditEmployee is null)
        {
            StatusBanner = "Vui lòng chọn nhân viên để lưu chấm công.";
            return;
        }

        if (!HrAttendanceViewModel.TryParseOptionalHourMinute(EditCheckInText, out TimeSpan? inTime))
        {
            StatusBanner = "Giờ VÀO sai định dạng HH:mm.";
            return;
        }

        if (!HrAttendanceViewModel.TryParseOptionalHourMinute(EditCheckOutText, out TimeSpan? outTime))
        {
            StatusBanner = "Giờ RA sai định dạng HH:mm.";
            return;
        }

        AttendanceRecordEntity saved = await _hrService.SaveCustomAttendanceAsync(
            EditEmployee.Id,
            DateOnly.FromDateTime(EditWorkDate),
            inTime,
            outTime,
            EditStatus,
            EditWorkDayUnits,
            EditOvertimeHours,
            EditNotes);

        if (!_session.IsDirectorNode)
        {
            _ = await _collabClient.PushAttendanceToDirectorsAsync(saved);
        }

        StatusBanner = $"Đã cập nhật chấm công {saved.EmployeeCode} ngày {saved.DateKey} ({saved.WorkedHours:F2}h).";
        await RefreshAsync();
        await LoadEditorForCurrentSelectionAsync();
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task DeleteEditedDayAsync()
    {
        if (EditEmployee is null)
        {
            StatusBanner = "Vui lòng chọn nhân viên.";
            return;
        }

        DateOnly workDate = DateOnly.FromDateTime(EditWorkDate);
        string dateKey = workDate.ToString("yyyy-MM-dd");
        AttendanceRecordEntity? rec = DailyRecords
            .Select(r => r.Entity)
            .FirstOrDefault(r => r.EmployeeProfileId == EditEmployee.Id && r.DateKey == dateKey)
            ?? await _hrService.GetAttendanceRecordForDayAsync(EditEmployee.Id, workDate);

        if (rec is null)
        {
            StatusBanner = "Ngày này chưa có chấm công để xóa.";
            return;
        }

        if (ConfirmCallback is not null &&
            !ConfirmCallback($"Xóa chấm công ngày {workDate:dd/MM/yyyy} của {EditEmployee.FullName}?", "Xác nhận xóa chấm công"))
        {
            return;
        }

        AttendanceRecordEntity? deleted = await _hrService.DeleteAttendanceRecordAsync(rec.Id);
        if (deleted is not null && !_session.IsDirectorNode)
        {
            _ = await _collabClient.PushAttendanceToDirectorsAsync(deleted);
        }

        StatusBanner = $"Đã xóa chấm công ngày {rec.DateKey} của {rec.EmployeeName}.";
        await RefreshAsync();
        await LoadEditorForCurrentSelectionAsync();
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task DeleteSelectedRecordAsync()
    {
        if (SelectedRecord is null)
        {
            return;
        }

        AttendanceRecordEntity? deleted = await _hrService.DeleteAttendanceRecordAsync(SelectedRecord.Id);
        if (deleted is not null && !_session.IsDirectorNode)
        {
            _ = await _collabClient.PushAttendanceToDirectorsAsync(deleted);
        }

        StatusBanner = $"Đã xóa dòng chấm công ngày {SelectedRecord.DateKey} của {SelectedRecord.EmployeeName}.";
        await RefreshAsync();
        await LoadEditorForCurrentSelectionAsync();
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        int year = Math.Clamp(SelectedYear, 2000, 2100);
        int month = Math.Clamp(SelectedMonth, 1, 12);
        var dialog = new SaveFileDialog
        {
            Title = "Xuất Bảng chấm công Chi tiết Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"BangChamCongFull_{year}_{month:D2}.xlsx"
        };

        if (dialog.ShowDialog() == true)
        {
            IReadOnlyList<MonthlyAttendanceSummaryRow> summaries = await _hrService.GetMonthlySummaryAsync(year, month);
            _excelExporter.ExportMonthlyReport(
                dialog.FileName,
                year,
                month,
                _session.CompanyName,
                summaries,
                DailyRecords.Select(r => r.Entity).ToArray());

            StatusBanner = $"Đã xuất bảng chấm công Excel: {dialog.FileName}";
        }
    }
}
