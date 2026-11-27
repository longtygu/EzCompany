using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Tasks;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed partial class SelectableSubtaskItem : ObservableObject
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    [ObservableProperty] private bool _isDone;
    public Action? ChangedCallback { get; init; }

    partial void OnIsDoneChanged(bool value) => ChangedCallback?.Invoke();
}

public sealed partial class TaskManagementViewModel : ObservableObject
{
    private readonly WorkTaskService _taskService;
    private readonly LanTaskClient _taskClient;
    private readonly PeerTaskSyncService _peerTaskSync;
    private readonly ILocalStoragePaths _paths;
    private readonly IEventBus _eventBus;

    public TaskManagementViewModel(
        WorkTaskService taskService,
        LanTaskClient taskClient,
        PeerTaskSyncService peerTaskSync,
        ILocalStoragePaths paths,
        IEventBus eventBus)
    {
        _taskService = taskService;
        _taskClient = taskClient;
        _peerTaskSync = peerTaskSync;
        _paths = paths;
        _eventBus = eventBus;

        _eventBus.Subscribe<WorkTaskUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<PeerDirectoryEntryDto> Peers { get; } = [];
    public ObservableCollection<SelectablePeerMember> SelectableAssignees { get; } = [];
    public ObservableCollection<WorkTaskEntity> AssignedToMeTasks { get; } = [];
    public ObservableCollection<WorkTaskEntity> AssignedByMeTasks { get; } = [];
    public ObservableCollection<WorkTaskEntity> FilteredInboxTasks { get; } = [];
    public ObservableCollection<WorkTaskEntity> FilteredOutboxTasks { get; } = [];
    public ObservableCollection<WorkTaskHistoryEntity> TaskHistories { get; } = [];
    public ObservableCollection<SelectableSubtaskItem> InboxSubtasks { get; } = [];

    public IReadOnlyList<string> AvailablePriorities { get; } = WorkTaskPriorities.All;
    public IReadOnlyList<string> AvailableRecurrences { get; } = WorkTaskRecurrenceRules.All;
    public IReadOnlyList<string> StatusFilterOptions { get; } = ["ALL", WorkTaskStatuses.Pending, WorkTaskStatuses.InProgress, WorkTaskStatuses.HandedOver, WorkTaskStatuses.Completed, WorkTaskStatuses.RevisionRequested];
    public IReadOnlyList<string> PriorityFilterOptions { get; } = ["ALL", .. WorkTaskPriorities.All];

    [ObservableProperty] private PeerDirectoryEntryDto? _selectedPeer;
    [ObservableProperty] private bool _assignToMultiplePeers;
    [ObservableProperty] private string _newTaskTitle = string.Empty;
    [ObservableProperty] private string _newTaskDescription = string.Empty;
    [ObservableProperty] private string _newTaskPriority = WorkTaskPriorities.Normal;
    [ObservableProperty] private string _newTaskRecurrence = WorkTaskRecurrenceRules.None;
    [ObservableProperty] private DateTime? _newTaskDueDate = DateTime.Today.AddDays(1);
    [ObservableProperty] private string _newTaskAttachmentPath = string.Empty;

    [ObservableProperty] private string _taskSearchQuery = string.Empty;
    [ObservableProperty] private string _taskStatusFilter = "ALL";
    [ObservableProperty] private string _taskPriorityFilter = "ALL";
    [ObservableProperty] private string _newSubtaskTitle = string.Empty;
    private bool _suppressSubtaskSave;

    [ObservableProperty] private WorkTaskEntity? _selectedInboxTask;
    [ObservableProperty] private string _handoverNotes = string.Empty;
    [ObservableProperty] private string _handoverAttachmentPath = string.Empty;

    [ObservableProperty] private WorkTaskEntity? _selectedOutboxTask;
    [ObservableProperty] private string _reviewNotes = string.Empty;

    [ObservableProperty] private string _statusBanner = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event EventHandler? RefreshRequested;

    partial void OnTaskSearchQueryChanged(string value) => RebuildFilteredLists();
    partial void OnTaskStatusFilterChanged(string value) => RebuildFilteredLists();
    partial void OnTaskPriorityFilterChanged(string value) => RebuildFilteredLists();

    partial void OnSelectedInboxTaskChanged(WorkTaskEntity? value)
    {
        ReloadInboxSubtasks();
    }

    private void ReloadInboxSubtasks()
    {
        _suppressSubtaskSave = true;
        try
        {
            InboxSubtasks.Clear();
            if (SelectedInboxTask is null)
            {
                return;
            }

            foreach (WorkTaskSubtaskItem sub in WorkTaskService.ParseSubtasks(SelectedInboxTask.SubtasksJson))
            {
                InboxSubtasks.Add(new SelectableSubtaskItem
                {
                    Id = sub.Id,
                    Title = sub.Title,
                    IsDone = sub.IsDone,
                    ChangedCallback = () => _ = SaveInboxSubtasksAsync()
                });
            }
        }
        finally
        {
            _suppressSubtaskSave = false;
        }
    }

    private async Task SaveInboxSubtasksAsync()
    {
        if (_suppressSubtaskSave || SelectedInboxTask is null)
        {
            return;
        }

        WorkTaskSubtaskItem[] items = InboxSubtasks
            .Select(s => new WorkTaskSubtaskItem(s.Id, s.Title, s.IsDone))
            .ToArray();
        WorkTaskEntity updated = await _taskService.UpdateSubtasksAsync(SelectedInboxTask.Id, items);
        SelectedInboxTask.SubtasksJson = updated.SubtasksJson;
        _ = await _peerTaskSync.FlushPendingTasksOnceAsync();
    }

    [RelayCommand]
    private async Task AddSubtaskAsync()
    {
        if (SelectedInboxTask is null || string.IsNullOrWhiteSpace(NewSubtaskTitle))
        {
            return;
        }

        InboxSubtasks.Add(new SelectableSubtaskItem
        {
            Id = Guid.NewGuid(),
            Title = NewSubtaskTitle.Trim(),
            IsDone = false,
            ChangedCallback = () => _ = SaveInboxSubtasksAsync()
        });
        NewSubtaskTitle = string.Empty;
        await SaveInboxSubtasksAsync();
    }

    [RelayCommand]
    private async Task DeleteSubtaskAsync(SelectableSubtaskItem? item)
    {
        if (item is null || SelectedInboxTask is null)
        {
            return;
        }

        InboxSubtasks.Remove(item);
        await SaveInboxSubtasksAsync();
    }

    private void RebuildFilteredLists()
    {
        string q = ChatWorkspaceViewModel.NormalizeForSearch(TaskSearchQuery);

        bool Matches(WorkTaskEntity t)
        {
            if (!string.IsNullOrEmpty(q) &&
                !ChatWorkspaceViewModel.NormalizeForSearch(t.Title).Contains(q, StringComparison.Ordinal) &&
                !ChatWorkspaceViewModel.NormalizeForSearch(t.AssignerFullName).Contains(q, StringComparison.Ordinal) &&
                !ChatWorkspaceViewModel.NormalizeForSearch(t.AssigneeFullName).Contains(q, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(TaskStatusFilter, "ALL", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(t.Status, TaskStatusFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(TaskPriorityFilter, "ALL", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(t.Priority, TaskPriorityFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        FilteredInboxTasks.Clear();
        foreach (WorkTaskEntity t in AssignedToMeTasks.Where(Matches))
        {
            FilteredInboxTasks.Add(t);
        }

        FilteredOutboxTasks.Clear();
        foreach (WorkTaskEntity t in AssignedByMeTasks.Where(Matches))
        {
            FilteredOutboxTasks.Add(t);
        }
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IsBusy = true;
        try
        {
            string? prevPeerMachine = SelectedPeer?.MachineId;
            IReadOnlyList<PeerDirectoryEntryDto> peers = await _taskClient.GetCompanyPeerDirectoryAsync();
            var prevChecked = SelectableAssignees.Where(a => a.IsSelected).Select(a => a.MachineId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Peers.Clear();
            SelectableAssignees.Clear();
            foreach (PeerDirectoryEntryDto p in peers)
            {
                Peers.Add(p);
                SelectableAssignees.Add(new SelectablePeerMember
                {
                    MachineId = p.MachineId,
                    FullName = p.FullName,
                    RoleDisplay = CompanyRoles.FormatRolesBilingual(p.Role),
                    IsOnline = p.IsOnline,
                    IsSelected = prevChecked.Contains(p.MachineId)
                });
            }
            SelectedPeer = Peers.FirstOrDefault(p => p.MachineId == prevPeerMachine)
                           ?? Peers.FirstOrDefault();

            Guid? prevInboxId = SelectedInboxTask?.Id;
            IReadOnlyList<WorkTaskEntity> inbox = await _taskService.GetAssignedToMeTasksAsync(includeCompleted: true);
            AssignedToMeTasks.Clear();
            foreach (WorkTaskEntity t in inbox)
            {
                AssignedToMeTasks.Add(t);
            }

            Guid? prevOutboxId = SelectedOutboxTask?.Id;
            IReadOnlyList<WorkTaskEntity> outbox = await _taskService.GetAssignedByMeTasksAsync();
            AssignedByMeTasks.Clear();
            foreach (WorkTaskEntity t in outbox)
            {
                AssignedByMeTasks.Add(t);
            }

            RebuildFilteredLists();
            SelectedInboxTask = FilteredInboxTasks.FirstOrDefault(t => t.Id == prevInboxId)
                                ?? FilteredInboxTasks.FirstOrDefault();
            SelectedOutboxTask = FilteredOutboxTasks.FirstOrDefault(t => t.Id == prevOutboxId)
                                 ?? FilteredOutboxTasks.FirstOrDefault();
            ReloadInboxSubtasks();

            IReadOnlyList<WorkTaskHistoryEntity> histories = await _taskService.GetTaskHistoriesAsync(null, 150);
            TaskHistories.Clear();
            foreach (WorkTaskHistoryEntity h in histories)
            {
                TaskHistories.Add(h);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void BrowseNewTaskAttachment()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn tệp đính kèm giao việc (tối đa 10 MB)",
            Filter = "Tất cả tệp (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            NewTaskAttachmentPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task AssignTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle))
        {
            StatusBanner = "Vui lòng nhập tiêu đề công việc.";
            return;
        }

        List<PeerDirectoryEntryDto> targets = [];
        if (AssignToMultiplePeers)
        {
            var selectedIds = SelectableAssignees.Where(s => s.IsSelected).Select(s => s.MachineId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            targets.AddRange(Peers.Where(p => selectedIds.Contains(p.MachineId)));
        }
        else if (SelectedPeer is not null)
        {
            targets.Add(SelectedPeer);
        }

        if (targets.Count == 0)
        {
            StatusBanner = "Vui lòng chọn ít nhất một nhân sự nhận việc.";
            return;
        }

        DateTimeOffset? dueUtc = NewTaskDueDate.HasValue
            ? new DateTimeOffset(NewTaskDueDate.Value.Date.AddHours(17).AddMinutes(30), DateTimeOffset.Now.Offset).ToUniversalTime()
            : null;

        string? attachPath = string.IsNullOrWhiteSpace(NewTaskAttachmentPath) ? null : NewTaskAttachmentPath;
        foreach (PeerDirectoryEntryDto target in targets)
        {
            await _taskService.CreateAssignedTaskAsync(
                target.MachineId,
                target.Username,
                target.FullName.Replace(" (Máy này)", string.Empty, StringComparison.Ordinal),
                target.Role,
                NewTaskTitle,
                NewTaskDescription,
                NewTaskPriority,
                dueUtc,
                attachPath,
                recurrenceRule: NewTaskRecurrence);
        }

        int synced = await _peerTaskSync.FlushPendingTasksOnceAsync();
        NewTaskTitle = string.Empty;
        NewTaskDescription = string.Empty;
        NewTaskAttachmentPath = string.Empty;

        StatusBanner = targets.Count > 1
            ? $"Đã giao việc lặp lại '{NewTaskRecurrence}' cho {targets.Count} người (đã đồng bộ ngay: {synced})."
            : string.Equals(targets[0].MachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase)
                ? "Đã tạo công việc cho chính bạn."
                : (synced > 0
                    ? $"Đã giao việc trực tiếp qua LAN tới {targets[0].FullName}."
                    : $"Máy {targets[0].FullName} đang ngoại tuyến — đã đưa vào hàng đợi và sẽ tự gửi khi Online.");

        await RefreshAllAsync();
    }

    [RelayCommand]
    private void OpenSelectedInboxAssignmentAttachment()
    {
        if (SelectedInboxTask is null || string.IsNullOrWhiteSpace(SelectedInboxTask.AssignmentVaultPath))
        {
            StatusBanner = "Công việc này không có tệp giao việc đính kèm.";
            return;
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            _paths.VaultDirectory,
            SelectedInboxTask.AssignmentVaultPath.Replace('/', Path.DirectorySeparatorChar)));

        if (!File.Exists(fullPath))
        {
            StatusBanner = "Không tìm thấy tệp giao việc trong Vault cục bộ.";
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task StartInboxTaskAsync()
    {
        if (SelectedInboxTask is null)
        {
            StatusBanner = "Vui lòng chọn một công việc được giao.";
            return;
        }

        await _taskService.StartTaskAsync(SelectedInboxTask.Id);
        _ = await _peerTaskSync.FlushPendingTasksOnceAsync();
        StatusBanner = $"Đã cập nhật trạng thái đang làm: {SelectedInboxTask.Title}";
        await RefreshAllAsync();
    }

    [RelayCommand]
    private void BrowseHandoverAttachment()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn tệp kết quả bàn giao (tối đa 10 MB)",
            Filter = "Tất cả tệp (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            HandoverAttachmentPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task SubmitHandoverAsync()
    {
        if (SelectedInboxTask is null)
        {
            StatusBanner = "Chọn công việc cần bàn giao kết quả.";
            return;
        }

        try
        {
            await _taskService.SubmitHandoverAsync(
                SelectedInboxTask.Id,
                HandoverNotes,
                string.IsNullOrWhiteSpace(HandoverAttachmentPath) ? null : HandoverAttachmentPath);

            int synced = await _peerTaskSync.FlushPendingTasksOnceAsync();
            HandoverNotes = string.Empty;
            HandoverAttachmentPath = string.Empty;

            StatusBanner = synced > 0
                ? "Đã bàn giao kết quả thành công qua LAN."
                : "Đã lưu bàn giao cục bộ và sẽ tự động chuyển qua LAN khi người giao Online.";
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Không thể bàn giao: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApproveOutboxHandoverAsync()
    {
        if (SelectedOutboxTask is null)
        {
            StatusBanner = "Chọn công việc đã giao cần nghiệm thu.";
            return;
        }

        await _taskService.ReviewHandoverAsync(SelectedOutboxTask.Id, approveComplete: true, ReviewNotes);
        _ = await _peerTaskSync.FlushPendingTasksOnceAsync();
        ReviewNotes = string.Empty;
        StatusBanner = $"Đã nghiệm thu hoàn thành công việc: {SelectedOutboxTask.Title}";
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task RequestOutboxRevisionAsync()
    {
        if (SelectedOutboxTask is null)
        {
            StatusBanner = "Chọn công việc cần yêu cầu làm lại.";
            return;
        }

        await _taskService.ReviewHandoverAsync(SelectedOutboxTask.Id, approveComplete: false, ReviewNotes);
        _ = await _peerTaskSync.FlushPendingTasksOnceAsync();
        ReviewNotes = string.Empty;
        StatusBanner = $"Đã gửi yêu cầu chỉnh sửa lại: {SelectedOutboxTask.Title}";
        await RefreshAllAsync();
    }

    [RelayCommand]
    private void OpenSelectedOutboxAttachment()
    {
        if (SelectedOutboxTask is null || string.IsNullOrWhiteSpace(SelectedOutboxTask.HandoverVaultPath))
        {
            StatusBanner = "Công việc này chưa có tệp bàn giao đính kèm.";
            return;
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            _paths.VaultDirectory,
            SelectedOutboxTask.HandoverVaultPath.Replace('/', Path.DirectorySeparatorChar)));

        if (!File.Exists(fullPath))
        {
            StatusBanner = "Không tìm thấy tệp bàn giao trong Vault cục bộ.";
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        int count = await _peerTaskSync.FlushPendingTasksOnceAsync();
        StatusBanner = count > 0
            ? $"Đã đồng bộ {count} công việc đang chờ qua LAN."
            : "Tất cả công việc đã đồng bộ hoặc máy nhận hiện chưa Online.";
        await RefreshAllAsync();
    }
}
