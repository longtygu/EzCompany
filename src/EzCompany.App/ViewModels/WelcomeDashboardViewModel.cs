using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Tasks;

namespace EzCompany.App.ViewModels;

public sealed partial class WelcomeDashboardViewModel : ObservableObject
{
    private readonly ISessionContext _session;
    private readonly WorkTaskService _taskService;
    private readonly PeerTaskSyncService _peerTaskSync;
    private readonly IEventBus _eventBus;

    public WelcomeDashboardViewModel(
        ISessionContext session,
        WorkTaskService taskService,
        PeerTaskSyncService peerTaskSync,
        IEventBus eventBus)
    {
        _session = session;
        _taskService = taskService;
        _peerTaskSync = peerTaskSync;
        _eventBus = eventBus;

        _eventBus.Subscribe<WorkTaskUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
        _eventBus.Subscribe<UserProfileUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<WorkTaskEntity> TodaySelfTasks { get; } = [];
    public ObservableCollection<WorkTaskEntity> AssignedToMeTasks { get; } = [];
    public ObservableCollection<WorkTaskEntity> CompletedTasksHistory { get; } = [];

    public IReadOnlyList<string> AvailablePriorities { get; } = WorkTaskPriorities.All;

    [ObservableProperty] private string _greetingText = "Xin chào";
    [ObservableProperty] private string _userRoleAndCompanyText = string.Empty;
    [ObservableProperty] private string _solarDateText = string.Empty;
    [ObservableProperty] private string _lunarDateText = string.Empty;

    [ObservableProperty] private string _newSelfTaskTitle = string.Empty;
    [ObservableProperty] private string _newSelfTaskPriority = WorkTaskPriorities.Normal;

    [ObservableProperty] private WorkTaskEntity? _selectedAssignedTask;
    [ObservableProperty] private string _quickHandoverNotes = string.Empty;

    [ObservableProperty] private int _totalTrackedCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _selfTodayCount;
    [ObservableProperty] private int _assignedOpenCount;
    [ObservableProperty] private int _overdueCount;
    [ObservableProperty] private double _completionPercent;
    [ObservableProperty] private string _completionPercentText = "0%";
    [ObservableProperty] private string _statusBanner = string.Empty;

    public event EventHandler? RefreshRequested;
    public event EventHandler? OpenTaskManagementRequested;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        GreetingText = string.IsNullOrWhiteSpace(_session.FullName)
            ? "Xin chào"
            : $"Xin chào {_session.FullName}";

        UserRoleAndCompanyText = $"{_session.CompanyName}  •  Vai trò: {_session.Role} (@{_session.Username})";

        VietnameseLunarDate lunar = VietnameseLunarCalendar.Today();
        SolarDateText = $"Dương lịch: {lunar.FormattedSolar}";
        LunarDateText = $"Âm lịch: {lunar.FormattedLunar}";

        if (!_session.IsAuthenticated)
        {
            return;
        }

        IReadOnlyList<WorkTaskEntity> todaySelf = await _taskService.GetTodaySelfTasksAsync();
        IReadOnlyList<WorkTaskEntity> assigned = await _taskService.GetAssignedToMeTasksAsync(includeCompleted: false);
        IReadOnlyList<WorkTaskEntity> completedHistory = await _taskService.GetCompletedTasksHistoryAsync(30);
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

        TotalTrackedCount = progress.TotalTrackedTasks;
        CompletedCount = progress.CompletedCount;
        SelfTodayCount = progress.SelfTodayCount;
        AssignedOpenCount = progress.AssignedOpenCount;
        OverdueCount = progress.OverdueCount;
        CompletionPercent = progress.CompletionPercent;
        CompletionPercentText = $"{progress.CompletionPercent:F0}% ({progress.CompletedCount}/{progress.TotalTrackedTasks})";
    }

    [RelayCommand]
    private async Task AddSelfTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSelfTaskTitle))
        {
            StatusBanner = "Vui lòng nhập tên task hôm nay trước khi thêm.";
            return;
        }

        try
        {
            await _taskService.CreateSelfTaskAsync(NewSelfTaskTitle, string.Empty, NewSelfTaskPriority, null);
            NewSelfTaskTitle = string.Empty;
            StatusBanner = "Đã thêm task hôm nay.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Không thể thêm task: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleSelfTaskAsync(WorkTaskEntity? task)
    {
        if (task is null)
        {
            return;
        }

        await _taskService.ToggleSelfTaskCompleteAsync(task.Id);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteSelfTaskAsync(WorkTaskEntity? task)
    {
        if (task is null)
        {
            return;
        }

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
    private void OpenFullTaskWorkspace()
    {
        OpenTaskManagementRequested?.Invoke(this, EventArgs.Empty);
    }
}
