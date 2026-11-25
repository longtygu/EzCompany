using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Tasks;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

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
    public ObservableCollection<WorkTaskEntity> AssignedToMeTasks { get; } = [];
    public ObservableCollection<WorkTaskEntity> AssignedByMeTasks { get; } = [];
    public ObservableCollection<WorkTaskHistoryEntity> TaskHistories { get; } = [];

    public IReadOnlyList<string> AvailablePriorities { get; } = WorkTaskPriorities.All;

    [ObservableProperty] private PeerDirectoryEntryDto? _selectedPeer;
    [ObservableProperty] private string _newTaskTitle = string.Empty;
    [ObservableProperty] private string _newTaskDescription = string.Empty;
    [ObservableProperty] private string _newTaskPriority = WorkTaskPriorities.Normal;
    [ObservableProperty] private DateTime? _newTaskDueDate = DateTime.Today.AddDays(1);

    [ObservableProperty] private WorkTaskEntity? _selectedInboxTask;
    [ObservableProperty] private string _handoverNotes = string.Empty;
    [ObservableProperty] private string _handoverAttachmentPath = string.Empty;

    [ObservableProperty] private WorkTaskEntity? _selectedOutboxTask;
    [ObservableProperty] private string _reviewNotes = string.Empty;

    [ObservableProperty] private string _statusBanner = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event EventHandler? RefreshRequested;

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IsBusy = true;
        try
        {
            string? prevPeerMachine = SelectedPeer?.MachineId;
            IReadOnlyList<PeerDirectoryEntryDto> peers = await _taskClient.GetCompanyPeerDirectoryAsync();
            Peers.Clear();
            foreach (PeerDirectoryEntryDto p in peers)
            {
                Peers.Add(p);
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
            SelectedInboxTask = AssignedToMeTasks.FirstOrDefault(t => t.Id == prevInboxId)
                                ?? AssignedToMeTasks.FirstOrDefault();

            Guid? prevOutboxId = SelectedOutboxTask?.Id;
            IReadOnlyList<WorkTaskEntity> outbox = await _taskService.GetAssignedByMeTasksAsync();
            AssignedByMeTasks.Clear();
            foreach (WorkTaskEntity t in outbox)
            {
                AssignedByMeTasks.Add(t);
            }
            SelectedOutboxTask = AssignedByMeTasks.FirstOrDefault(t => t.Id == prevOutboxId)
                                 ?? AssignedByMeTasks.FirstOrDefault();

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
    private async Task AssignTaskAsync()
    {
        if (SelectedPeer is null)
        {
            StatusBanner = "Vui lòng chọn nhân sự nhận việc.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewTaskTitle))
        {
            StatusBanner = "Vui lòng nhập tiêu đề công việc.";
            return;
        }

        DateTimeOffset? dueUtc = NewTaskDueDate.HasValue
            ? new DateTimeOffset(NewTaskDueDate.Value.Date.AddHours(17).AddMinutes(30), DateTimeOffset.Now.Offset).ToUniversalTime()
            : null;

        await _taskService.CreateAssignedTaskAsync(
            SelectedPeer.MachineId,
            SelectedPeer.Username,
            SelectedPeer.FullName.Replace(" (Máy này)", string.Empty, StringComparison.Ordinal),
            SelectedPeer.Role,
            NewTaskTitle,
            NewTaskDescription,
            NewTaskPriority,
            dueUtc);

        int synced = await _peerTaskSync.FlushPendingTasksOnceAsync();
        NewTaskTitle = string.Empty;
        NewTaskDescription = string.Empty;

        StatusBanner = string.Equals(SelectedPeer.MachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase)
            ? "Đã tạo công việc cho chính bạn."
            : (synced > 0
                ? $"Đã giao việc trực tiếp qua LAN tới {SelectedPeer.FullName}."
                : $"Máy {SelectedPeer.FullName} đang ngoại tuyến — đã đưa vào hàng đợi và sẽ tự gửi khi Online.");

        await RefreshAllAsync();
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
