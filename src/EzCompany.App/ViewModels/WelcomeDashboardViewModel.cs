using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Collaboration;
using EzCompany.Core.LanAgent.Tasks;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.App.ViewModels;

public sealed class RoleSubTabModel
{
    public required string RoleKey { get; init; }
    public required string TabHeader { get; init; }
    public required string Headline { get; init; }
    public required string MetricsSummary { get; init; }
    public required string ToolButtonText { get; init; }
    public required string TargetNavKey { get; init; }
    public string SecondaryButtonText { get; init; } = string.Empty;
    public string SecondaryNavKey { get; init; } = string.Empty;
    public bool HasSecondaryButton => !string.IsNullOrWhiteSpace(SecondaryButtonText);
}

public sealed partial class WelcomeDashboardViewModel : ObservableObject
{
    private readonly ISessionContext _session;
    private readonly WorkTaskService _taskService;
    private readonly TimelinePlanService _timelineService;
    private readonly CompanyBrandingService _branding;
    private readonly HrAttendanceService _hrService;
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly PeerTaskSyncService _peerTaskSync;
    private readonly PeerCollabSyncService _collabSync;
    private readonly IEventBus _eventBus;

    public WelcomeDashboardViewModel(
        ISessionContext session,
        WorkTaskService taskService,
        TimelinePlanService timelineService,
        CompanyBrandingService branding,
        HrAttendanceService hrService,
        IDbContextFactory<AppDbContext> appDbFactory,
        PeerTaskSyncService peerTaskSync,
        PeerCollabSyncService collabSync,
        IEventBus eventBus)
    {
        _session = session;
        _taskService = taskService;
        _timelineService = timelineService;
        _branding = branding;
        _hrService = hrService;
        _appDbFactory = appDbFactory;
        _peerTaskSync = peerTaskSync;
        _collabSync = collabSync;
        _eventBus = eventBus;

        _eventBus.Subscribe<WorkTaskUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
        _eventBus.Subscribe<UserProfileUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
        _eventBus.Subscribe<TimelinePlanUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
        _eventBus.Subscribe<CompanyLogoUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<WorkTaskEntity> TodaySelfTasks { get; } = [];
    public ObservableCollection<WorkTaskEntity> AssignedToMeTasks { get; } = [];
    public ObservableCollection<WorkTaskEntity> CompletedTasksHistory { get; } = [];
    public ObservableCollection<TimelinePlanEntity> TimelinePlans { get; } = [];
    public ObservableCollection<RoleSubTabModel> RoleSubTabs { get; } = [];

    public IReadOnlyList<string> AvailablePriorities { get; } = WorkTaskPriorities.All;

    [ObservableProperty] private string _greetingText = "Xin chào";
    [ObservableProperty] private string _userRoleAndCompanyText = string.Empty;
    [ObservableProperty] private string _solarDateText = string.Empty;
    [ObservableProperty] private string _lunarDateText = string.Empty;
    [ObservableProperty] private BitmapImage? _companyLogoImage;

    // Self-created task & quick handover
    [ObservableProperty] private string _newSelfTaskTitle = string.Empty;
    [ObservableProperty] private string _newSelfTaskPriority = WorkTaskPriorities.Normal;
    [ObservableProperty] private WorkTaskEntity? _selectedAssignedTask;
    [ObservableProperty] private string _quickHandoverNotes = string.Empty;

    // Parallel Horizontal Timeline state
    private DateOnly _timelineAnchorDate = DateOnly.FromDateTime(DateTime.Today);
    private TimelineZoomMode _timelineZoomMode = TimelineZoomMode.Normal30Days;
    private int _timelineTrackCount = TimelineTrackLayoutEngine.DefaultTrackCount;
    private double _timelineViewportWidth = 820.0;

    [ObservableProperty] private TimelineLayoutResult? _timelineLayout;
    [ObservableProperty] private string _timelineZoomLabel = "Mặc định 30 ngày";
    [ObservableProperty] private string _timelineTrackToggleLabel = "Mở rộng (5 đường)";
    [ObservableProperty] private string _timelineSummaryText = string.Empty;

    // Progress counters
    [ObservableProperty] private int _totalTrackedCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _selfTodayCount;
    [ObservableProperty] private int _assignedOpenCount;
    [ObservableProperty] private int _overdueCount;
    [ObservableProperty] private double _completionPercent;
    [ObservableProperty] private string _completionPercentText = "0%";
    [ObservableProperty] private string _statusBanner = string.Empty;

    public event EventHandler? RefreshRequested;
    public event EventHandler<string>? NavigateRequested;
    public event EventHandler<TimelinePlanEntity?>? OpenTimelineEditorRequested;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        GreetingText = string.IsNullOrWhiteSpace(_session.FullName)
            ? "Xin chào"
            : $"Xin chào {_session.FullName}";

        UserRoleAndCompanyText = $"{_session.CompanyName}  •  Chức danh: {CompanyRoles.FormatRolesBilingual(_session.Role)} (@{_session.Username})";

        VietnameseLunarDate lunar = VietnameseLunarCalendar.Today();
        SolarDateText = $"Dương lịch: {lunar.FormattedSolar}";
        LunarDateText = $"Âm lịch: {lunar.FormattedLunar}";

        CompanyLogoImage = CompanyLogoImageHelper.LoadFromBytesOrDefault(await _branding.GetCompanyLogoBytesAsync());

        if (!_session.IsAuthenticated)
        {
            return;
        }

        IReadOnlyList<WorkTaskEntity> todaySelf = await _taskService.GetTodaySelfTasksAsync();
        IReadOnlyList<WorkTaskEntity> assigned = await _taskService.GetAssignedToMeTasksAsync(includeCompleted: false);
        IReadOnlyList<WorkTaskEntity> completedHistory = await _taskService.GetCompletedTasksHistoryAsync(30);
        IReadOnlyList<TimelinePlanEntity> plans = await _timelineService.GetActivePlansAsync();
        WorkTaskProgressSummary progress = await _taskService.GetProgressSummaryAsync();

        TodaySelfTasks.Clear();
        foreach (WorkTaskEntity item in todaySelf)
        {
            TodaySelfTasks.Add(item);
        }

        Guid? prevAssignedId = SelectedAssignedTask?.Id;
        AssignedToMeTasks.Clear();
        foreach (WorkTaskEntity item in assigned)
        {
            AssignedToMeTasks.Add(item);
        }
        SelectedAssignedTask = AssignedToMeTasks.FirstOrDefault(x => x.Id == prevAssignedId)
                               ?? AssignedToMeTasks.FirstOrDefault();

        CompletedTasksHistory.Clear();
        foreach (WorkTaskEntity item in completedHistory)
        {
            CompletedTasksHistory.Add(item);
        }

        TimelinePlans.Clear();
        foreach (TimelinePlanEntity p in plans)
        {
            TimelinePlans.Add(p);
        }
        RebuildTimelineLayout();

        TotalTrackedCount = progress.TotalTrackedTasks;
        CompletedCount = progress.CompletedCount;
        SelfTodayCount = progress.SelfTodayCount;
        AssignedOpenCount = progress.AssignedOpenCount;
        OverdueCount = progress.OverdueCount;
        CompletionPercent = progress.CompletionPercent;
        CompletionPercentText = $"{progress.CompletionPercent:F0}% ({progress.CompletedCount}/{progress.TotalTrackedTasks})";

        await RebuildRoleSubTabsAsync();
    }

    private async Task RebuildRoleSubTabsAsync()
    {
        RoleSubTabs.Clear();
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync();

        int deviceCount = await db.PairedDevices.AsNoTracking().CountAsync();
        int onlineCount = await db.PairedDevices.AsNoTracking().CountAsync(d => d.IsOnline);
        int invoiceCount = await db.AccountingInvoices.AsNoTracking().CountAsync();
        int invoiceIssueCount = await db.AccountingInvoices.AsNoTracking().CountAsync(i => i.HasMathMismatch || i.IsDuplicate);
        int contractCount = await db.LegalContractReviews.AsNoTracking().CountAsync();
        int pdfCount = await db.OfficeDocumentRecords.AsNoTracking().CountAsync();
        IReadOnlyList<EmployeeProfileEntity> emps = await _hrService.GetEmployeesAsync();
        string todayKey = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        int todayCheckIns = await db.AttendanceRecords.AsNoTracking().CountAsync(a => a.DateKey == todayKey);

        foreach (string role in CompanyRoles.ParseRoles(_session.Role))
        {
            RoleSubTabModel card = role switch
            {
                CompanyRoles.Director => new RoleSubTabModel
                {
                    RoleKey = role,
                    TabHeader = "Manager (Giám đốc)",
                    Headline = "Không gian Điều hành Manager (Giám đốc)",
                    MetricsSummary = $"Máy trạm LAN: {onlineCount}/{deviceCount} đang Online • Kế hoạch công ty: {TimelinePlans.Count} • Nhân sự: {emps.Count} • Chấm công hôm nay: {todayCheckIns}",
                    ToolButtonText = "Mở Quản trị Giám đốc",
                    TargetNavKey = "director.dashboard",
                    SecondaryButtonText = "Quản lý Nhân sự & Chấm công",
                    SecondaryNavKey = "core.hr"
                },
                CompanyRoles.Accounting => new RoleSubTabModel
                {
                    RoleKey = role,
                    TabHeader = "Accounting (Kế toán)",
                    Headline = "Bàn làm việc Kế toán (Accounting)",
                    MetricsSummary = $"Tổng hóa đơn XML đã lưu: {invoiceCount} • Cảnh báo lệch/trùng cần kiểm tra: {invoiceIssueCount}",
                    ToolButtonText = "Mở Công cụ Kế toán XML & Excel",
                    TargetNavKey = ModuleIds.AccountingInvoiceXml,
                    SecondaryButtonText = "Giao việc / Chat nội bộ",
                    SecondaryNavKey = "core.tasks"
                },
                CompanyRoles.Legal => new RoleSubTabModel
                {
                    RoleKey = role,
                    TabHeader = "Legal (Pháp chế)",
                    Headline = "Bàn làm việc Pháp chế (Legal)",
                    MetricsSummary = $"Tổng hợp đồng đã rà soát (.docx Diff): {contractCount} • Công việc đang mở: {AssignedOpenCount}",
                    ToolButtonText = "Mở Công cụ So sánh Hợp đồng",
                    TargetNavKey = ModuleIds.LegalContractDiff,
                    SecondaryButtonText = "Mở Chat Pháp chế / Nhóm",
                    SecondaryNavKey = "core.chat"
                },
                CompanyRoles.Hr => new RoleSubTabModel
                {
                    RoleKey = role,
                    TabHeader = "HR (Nhân sự)",
                    Headline = "Bàn làm việc Hành chính Nhân sự (HR)",
                    MetricsSummary = $"Tổng hồ sơ nhân viên: {emps.Count} • Đã chấm công hôm nay ({todayKey}): {todayCheckIns}",
                    ToolButtonText = "Mở Hồ sơ & Báo cáo Chấm công",
                    TargetNavKey = "core.hr",
                    SecondaryButtonText = "Mở Văn phòng PDF",
                    SecondaryNavKey = ModuleIds.OfficePdfStudio
                },
                _ => new RoleSubTabModel
                {
                    RoleKey = role,
                    TabHeader = "Staff (Nhân viên)",
                    Headline = "Bàn làm việc Nhân viên (Staff)",
                    MetricsSummary = $"Task hôm nay: {SelfTodayCount} • Việc được giao: {AssignedOpenCount} • Tài liệu PDF đã xử lý: {pdfCount}",
                    ToolButtonText = "Mở Công cụ Văn phòng PDF",
                    TargetNavKey = ModuleIds.OfficePdfStudio,
                    SecondaryButtonText = "Chấm công Hôm nay",
                    SecondaryNavKey = "core.hr"
                }
            };

            RoleSubTabs.Add(card);
        }
    }

    public void UpdateTimelineViewportWidth(double width)
    {
        if (width > 200 && Math.Abs(width - _timelineViewportWidth) > 24)
        {
            _timelineViewportWidth = width;
            RebuildTimelineLayout();
        }
    }

    private void RebuildTimelineLayout()
    {
        TimelineLayoutResult layout = TimelineTrackLayoutEngine.BuildLayout(
            TimelinePlans.ToArray(),
            _timelineAnchorDate,
            _timelineZoomMode,
            _timelineTrackCount,
            _timelineViewportWidth);

        TimelineLayout = layout;
        TimelineZoomLabel = _timelineZoomMode switch
        {
            TimelineZoomMode.Overview60Days => "Toàn cảnh 60 ngày",
            TimelineZoomMode.Detailed14Days => "Chi tiết 14 ngày",
            _ => "Mặc định 30 ngày"
        };
        TimelineTrackToggleLabel = _timelineTrackCount > TimelineTrackLayoutEngine.DefaultTrackCount
            ? $"Thu gọn ({TimelineTrackLayoutEngine.DefaultTrackCount} đường)"
            : $"Mở rộng ({TimelineTrackLayoutEngine.ExpandedTrackCount} đường)";

        string overflowNote = layout.OverflowPlanCount > 0
            ? $" • ⚠ {layout.OverflowPlanCount} kế hoạch trùng ngày chưa hiển thị (bấm Mở rộng 5 đường)"
            : string.Empty;
        TimelineSummaryText = $"Khoảng {layout.RangeStart:dd/MM/yyyy} → {layout.RangeEnd:dd/MM/yyyy} • Hiển thị {_timelineTrackCount} đường song song • {TimelinePlans.Count} kế hoạch{overflowNote} • Bấm trực tiếp lên thanh kế hoạch để xem/sửa.";
    }

    [RelayCommand]
    private void OpenTimelineEditor(TimelinePlanEntity? plan = null) =>
        OpenTimelineEditorRequested?.Invoke(this, plan);

    [RelayCommand]
    private void ZoomInTimeline()
    {
        _timelineZoomMode = _timelineZoomMode switch
        {
            TimelineZoomMode.Overview60Days => TimelineZoomMode.Normal30Days,
            TimelineZoomMode.Normal30Days => TimelineZoomMode.Detailed14Days,
            _ => TimelineZoomMode.Detailed14Days
        };
        RebuildTimelineLayout();
    }

    [RelayCommand]
    private void ZoomOutTimeline()
    {
        _timelineZoomMode = _timelineZoomMode switch
        {
            TimelineZoomMode.Detailed14Days => TimelineZoomMode.Normal30Days,
            TimelineZoomMode.Normal30Days => TimelineZoomMode.Overview60Days,
            _ => TimelineZoomMode.Overview60Days
        };
        RebuildTimelineLayout();
    }

    [RelayCommand]
    private void ResetTimelineZoom()
    {
        _timelineZoomMode = TimelineZoomMode.Normal30Days;
        _timelineAnchorDate = DateOnly.FromDateTime(DateTime.Today);
        _timelineTrackCount = TimelineTrackLayoutEngine.DefaultTrackCount;
        RebuildTimelineLayout();
    }

    [RelayCommand]
    private void ToggleTimelineTracks()
    {
        _timelineTrackCount = _timelineTrackCount <= TimelineTrackLayoutEngine.DefaultTrackCount
            ? TimelineTrackLayoutEngine.ExpandedTrackCount
            : TimelineTrackLayoutEngine.DefaultTrackCount;
        RebuildTimelineLayout();
    }

    [RelayCommand]
    private void ShiftTimelineBackward()
    {
        int step = _timelineZoomMode switch
        {
            TimelineZoomMode.Detailed14Days => 7,
            TimelineZoomMode.Overview60Days => 20,
            _ => 10
        };
        _timelineAnchorDate = _timelineAnchorDate.AddDays(-step);
        RebuildTimelineLayout();
    }

    [RelayCommand]
    private void ShiftTimelineForward()
    {
        int step = _timelineZoomMode switch
        {
            TimelineZoomMode.Detailed14Days => 7,
            TimelineZoomMode.Overview60Days => 20,
            _ => 10
        };
        _timelineAnchorDate = _timelineAnchorDate.AddDays(step);
        RebuildTimelineLayout();
    }

    [RelayCommand]
    private void JumpTimelineToday()
    {
        _timelineAnchorDate = DateOnly.FromDateTime(DateTime.Today);
        RebuildTimelineLayout();
    }

    [RelayCommand]
    private async Task AddSelfTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSelfTaskTitle))
        {
            StatusBanner = "Vui lòng nhập tên task hôm nay trước khi thêm.";
            return;
        }

        await _taskService.CreateSelfTaskAsync(NewSelfTaskTitle, string.Empty, NewSelfTaskPriority, null);
        NewSelfTaskTitle = string.Empty;
        StatusBanner = "Đã thêm task hôm nay.";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ToggleSelfTaskAsync(WorkTaskEntity? task)
    {
        if (task is null) return;
        await _taskService.ToggleSelfTaskCompleteAsync(task.Id);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteSelfTaskAsync(WorkTaskEntity? task)
    {
        if (task is null) return;
        await _taskService.DeleteSelfTaskAsync(task.Id);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task StartSelectedAssignedTaskAsync()
    {
        if (SelectedAssignedTask is null)
        {
            StatusBanner = "Chọn một task được giao để cập nhật.";
            return;
        }

        await _taskService.StartTaskAsync(SelectedAssignedTask.Id);
        _ = await _peerTaskSync.FlushPendingTasksOnceAsync();
        StatusBanner = $"Đã bắt đầu thực hiện: {SelectedAssignedTask.Title}";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SubmitQuickHandoverAsync()
    {
        if (SelectedAssignedTask is null)
        {
            StatusBanner = "Chọn một task được giao để gửi bàn giao nhanh.";
            return;
        }

        if (string.IsNullOrWhiteSpace(QuickHandoverNotes))
        {
            StatusBanner = "Nhập ghi chú kết quả bàn giao trước khi gửi.";
            return;
        }

        await _taskService.SubmitHandoverAsync(SelectedAssignedTask.Id, QuickHandoverNotes, null);
        int synced = await _peerTaskSync.FlushPendingTasksOnceAsync();
        QuickHandoverNotes = string.Empty;
        StatusBanner = synced > 0
            ? "Đã gửi bàn giao kết quả qua LAN tới người giao việc."
            : "Đã lưu bàn giao cục bộ và xếp hàng tự đồng bộ khi người giao việc online.";
        await RefreshAsync();
    }

    [RelayCommand]
    private void NavigateToKey(string? navKey)
    {
        if (!string.IsNullOrWhiteSpace(navKey))
        {
            NavigateRequested?.Invoke(this, navKey);
        }
    }

    [RelayCommand]
    private void OpenFullTaskWorkspace()
    {
        NavigateRequested?.Invoke(this, "core.tasks");
    }
}
