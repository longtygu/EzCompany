using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Collaboration;

namespace EzCompany.App.ViewModels;

public sealed partial class TimelineEditorViewModel : ObservableObject
{
    private readonly TimelinePlanService _timelineService;
    private readonly PeerCollabSyncService _collabSync;

    public TimelineEditorViewModel(TimelinePlanService timelineService, PeerCollabSyncService collabSync)
    {
        _timelineService = timelineService;
        _collabSync = collabSync;
    }

    public ObservableCollection<TimelinePlanEntity> Plans { get; } = [];
    public IReadOnlyList<string> AvailableCategories { get; } = TimelineCategories.All;
    public IReadOnlyList<string> AvailableStatuses { get; } = ["Planned", "InProgress", "Done"];

    [ObservableProperty] private Guid? _editingPlanId;
    [ObservableProperty] private string _editorTitle = "Tạo Kế hoạch / Lịch trình mới";
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _category = TimelineCategories.Plan;
    [ObservableProperty] private string _status = "Planned";
    [ObservableProperty] private DateTime? _startDate = DateTime.Today;
    [ObservableProperty] private DateTime? _endDate = DateTime.Today.AddDays(3);
    [ObservableProperty] private TimelinePlanEntity? _selectedPlan;
    [ObservableProperty] private string _statusBanner = string.Empty;

    public event EventHandler? PlansChanged;

    public async Task LoadAsync(TimelinePlanEntity? initialPlan = null)
    {
        IReadOnlyList<TimelinePlanEntity> items = await _timelineService.GetActivePlansAsync();
        Plans.Clear();
        foreach (TimelinePlanEntity p in items)
        {
            Plans.Add(p);
        }

        if (initialPlan is not null)
        {
            TimelinePlanEntity? match = Plans.FirstOrDefault(p => p.Id == initialPlan.Id);
            if (match is not null)
            {
                SelectedPlan = match;
                LoadPlanIntoForm(match);
                return;
            }
        }

        StartNewPlan();
    }

    partial void OnSelectedPlanChanged(TimelinePlanEntity? value)
    {
        if (value is not null)
        {
            LoadPlanIntoForm(value);
        }
    }

    [RelayCommand]
    private void StartNewPlan()
    {
        SelectedPlan = null;
        EditingPlanId = null;
        EditorTitle = "Tạo Kế hoạch / Lịch trình mới";
        Title = string.Empty;
        Description = string.Empty;
        Category = TimelineCategories.Plan;
        Status = "Planned";
        StartDate = DateTime.Today;
        EndDate = DateTime.Today.AddDays(3);
    }

    private void LoadPlanIntoForm(TimelinePlanEntity p)
    {
        EditingPlanId = p.Id;
        EditorTitle = $"Chỉnh sửa Kế hoạch: {p.Title}";
        Title = p.Title;
        Description = p.Description;
        Category = p.Category;
        Status = p.Status;
        StartDate = p.StartDateUtc.ToLocalTime().Date;
        EndDate = p.EndDateUtc.ToLocalTime().Date;
    }

    [RelayCommand]
    private async Task SavePlanAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            StatusBanner = "Vui lòng nhập tiêu đề kế hoạch / lịch trình.";
            return;
        }

        DateTime sLocal = (StartDate ?? DateTime.Today).Date;
        DateTime eLocal = (EndDate ?? sLocal).Date;
        if (eLocal < sLocal)
        {
            eLocal = sLocal;
        }

        DateTimeOffset sUtc = new DateTimeOffset(sLocal, DateTimeOffset.Now.Offset).ToUniversalTime();
        DateTimeOffset eUtc = new DateTimeOffset(eLocal.AddHours(17).AddMinutes(30), DateTimeOffset.Now.Offset).ToUniversalTime();

        TimelinePlanEntity saved = await _timelineService.AddOrUpdatePlanAsync(
            EditingPlanId,
            Title,
            Description,
            Category,
            sUtc,
            eUtc,
            Status);

        _ = await _collabSync.SyncCollaborationOnceAsync();
        StatusBanner = EditingPlanId.HasValue
            ? $"Đã cập nhật kế hoạch '{saved.Title}'."
            : $"Đã tạo kế hoạch '{saved.Title}' và đồng bộ LAN.";

        await LoadAsync(saved);
        PlansChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task CycleStatusAsync(TimelinePlanEntity? plan)
    {
        TimelinePlanEntity? target = plan ?? SelectedPlan;
        if (target is null)
        {
            return;
        }

        TimelinePlanEntity? updated = await _timelineService.CycleStatusAsync(target.Id);
        _ = await _collabSync.SyncCollaborationOnceAsync();
        await LoadAsync(updated);
        PlansChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task DeletePlanAsync(TimelinePlanEntity? plan)
    {
        TimelinePlanEntity? target = plan ?? SelectedPlan;
        if (target is null)
        {
            return;
        }

        await _timelineService.DeletePlanAsync(target.Id);
        _ = await _collabSync.SyncCollaborationOnceAsync();
        StatusBanner = $"Đã xóa kế hoạch '{target.Title}'.";
        await LoadAsync(null);
        PlansChanged?.Invoke(this, EventArgs.Empty);
    }
}
