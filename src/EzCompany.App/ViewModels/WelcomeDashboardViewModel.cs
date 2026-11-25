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
    public IReadOnlyList<string> AvailableTimelineCategories { get; } = TimelineCategories.All;

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

    // Shared Timeline Schedule
    [ObservableProperty] private string _newPlanTitle = string.Empty;
    [ObservableProperty] private string _newPlanDescription = string.Empty;
    [ObservableProperty] private string _newPlanCategory = TimelineCategories.Plan;
    [ObservableProperty] private DateTime? _newPlanStartDate = DateTime.Today;
    [ObservableProperty] private DateTime? _newPlanEndDate = DateTime.Today.AddDays(3);

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

    [RelayCommand]
    private async Task AddTimelinePlanAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlanTitle))
        {
            StatusBanner = "Vui lòng nhập tiêu đề kế hoạch / lịch trình.";
            return;
        }

        DateTime startLocal = NewPlanStartDate ?? DateTime.Today;
        DateTime endLocal = NewPlanEndDate ?? startLocal;
        DateTimeOffset startUtc = new DateTimeOffset(startLocal.Date, DateTimeOffset.Now.Offset).ToUniversalTime();
        DateTimeOffset endUtc = new DateTimeOffset(endLocal.Date.AddHours(17).AddMinutes(30), DateTimeOffset.Now.Offset).ToUniversalTime();

        await _timelineService.AddOrUpdatePlanAsync(
            null,
            NewPlanTitle,
            NewPlanDescription,
            NewPlanCategory,
            startUtc,
            endUtc);

        NewPlanTitle = string.Empty;
        NewPlanDescription = string.Empty;
        (_, int synced, _) = await _collabSync.SyncCollaborationOnceAsync();
        StatusBanner = synced >= 0
            ? "Đã bổ sung kế hoạch vào Lịch trình chung và đồng bộ LAN."
            : "Đã lưu kế hoạch vào Lịch trình chung.";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CycleTimelinePlanStatusAsync(TimelinePlanEntity? plan)
    {
        if (plan is null) return;
        await _timelineService.CycleStatusAsync(plan.Id);
        _ = await _collabSync.SyncCollaborationOnceAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteTimelinePlanAsync(TimelinePlanEntity? plan)
    {
        if (plan is null) return;
        await _timelineService.DeletePlanAsync(plan.Id);
        _ = await _collabSync.SyncCollaborationOnceAsync();
        await RefreshAsync();
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
