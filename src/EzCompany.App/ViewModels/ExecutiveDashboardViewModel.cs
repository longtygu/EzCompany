using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Pairing;
using EzCompany.Core.LanAgent.Recovery;
using EzCompany.Core.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed class SnapshotTimelineItem
{
    public required Guid Id { get; init; }
    public required string MachineId { get; init; }
    public required string Username { get; init; }
    public required DateTimeOffset CapturedAtUtc { get; init; }
    public required string Caption { get; init; }
    public BitmapImage? ThumbnailImage { get; init; }
}

public sealed partial class WorkstationCardItem : ObservableObject
{
    public required PairedDeviceEntity Entity { get; init; }
    public Action<WorkstationCardItem, bool>? ExpandedChangedCallback { get; init; }

    public string MachineId => Entity.MachineId;
    public string FullName => string.IsNullOrWhiteSpace(Entity.FullName) ? Entity.Username : Entity.FullName;
    public string Username => Entity.Username;
    public string RoleBilingual => CompanyRoles.FormatRolesBilingual(Entity.Role);
    public bool IsOnline => Entity.IsOnline;
    public bool IsOffline => !Entity.IsOnline;
    public string OnlineText => Entity.IsOnline ? "Online" : "Offline";
    public string AppVersionText => $"v{Entity.AppVersion}";
    public string PolicyVersionText => $"Policy v{Entity.CurrentPolicyVersion}";
    public string EndpointText => string.IsNullOrWhiteSpace(Entity.LastKnownIp) || Entity.LastKnownPort <= 0
        ? "—"
        : $"{Entity.LastKnownIp}:{Entity.LastKnownPort}";
    public string FreeDiskText => Entity.FreeDiskPercent > 0 ? $"{Entity.FreeDiskPercent:F1}%" : "—";
    public string StorageText => $"{(Entity.DbSizeBytes + Entity.VaultSizeBytes) / 1048576.0:F2} MB (DB {Entity.DbSizeBytes / 1048576.0:F2} • Vault {Entity.VaultSizeBytes / 1048576.0:F2})";
    public string LastSeenText => Entity.LastSeenUtc?.ToLocalTime().ToString("HH:mm dd/MM/yyyy") ?? "Chưa ghi nhận";
    public string LastBackupText => Entity.LastBackupUtc?.ToLocalTime().ToString("HH:mm dd/MM/yyyy") ?? "Chưa sao lưu";
    public string PairedText => $"{Entity.PairedAtUtc.ToLocalTime():dd/MM/yyyy} bởi {(string.IsNullOrWhiteSpace(Entity.PairedByAdminName) ? "—" : Entity.PairedByAdminName)}";
    public string PendingPolicyText => string.IsNullOrWhiteSpace(Entity.PendingPolicyJson)
        ? "Không có phân quyền chờ"
        : "Có phân quyền đang chờ đẩy (máy Offline)";

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value) => ExpandedChangedCallback?.Invoke(this, value);
}

public sealed record EmployeeTaskChartRow(
    string Name,
    int Completed,
    int Total,
    string SummaryText,
    double BarWidth);

public sealed record EmployeeAttendanceChartRow(
    string Name,
    double WorkedHours,
    int LateDays,
    string SummaryText,
    double BarWidth);

public sealed partial class ExecutiveDashboardViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly IDbContextFactory<TelemetryDbContext> _telDbFactory;
    private readonly PairingSessionManager _pairingManager;
    private readonly AccountRecoveryManager _recoveryManager;
    private readonly DirectorLanClient _lanClient;
    private readonly DirectorFleetSyncService _fleetSync;
    private readonly DirectorTelemetryPullService _telemetryPull;
    private readonly LeaveRequestService _leaveService;
    private readonly AccountingLedgerService _ledgerService;
    private readonly EzCompany.Core.LanAgent.Collaboration.LanCollaborationClient _collabClient;
    private readonly DirectorAutoBackupService _autoBackup;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly IEventBus _eventBus;

    public ExecutiveDashboardViewModel(
        IDbContextFactory<AppDbContext> appDbFactory,
        IDbContextFactory<TelemetryDbContext> telDbFactory,
        PairingSessionManager pairingManager,
        AccountRecoveryManager recoveryManager,
        DirectorLanClient lanClient,
        DirectorFleetSyncService fleetSync,
        DirectorTelemetryPullService telemetryPull,
        LeaveRequestService leaveService,
        AccountingLedgerService ledgerService,
        EzCompany.Core.LanAgent.Collaboration.LanCollaborationClient collabClient,
        DirectorAutoBackupService autoBackup,
        ILocalStoragePaths paths,
        ISessionContext session,
        IAuditLogger audit,
        IEventBus eventBus)
    {
        _appDbFactory = appDbFactory;
        _telDbFactory = telDbFactory;
        _pairingManager = pairingManager;
        _recoveryManager = recoveryManager;
        _lanClient = lanClient;
        _fleetSync = fleetSync;
        _telemetryPull = telemetryPull;
        _leaveService = leaveService;
        _ledgerService = ledgerService;
        _collabClient = collabClient;
        _autoBackup = autoBackup;
        _paths = paths;
        _session = session;
        _audit = audit;
        _eventBus = eventBus;

        DirectorAutoBackupConfig cfg = _autoBackup.GetConfig();
        _autoBackupEnabled = cfg.Enabled;
        _autoBackupIntervalHours = cfg.IntervalHours;
        _autoBackupRetentionCount = cfg.RetentionCountPerDevice;
        _autoBackupLastSummary = cfg.LastRunSummary;

        _eventBus.Subscribe<PasswordResetRequestedEvent>(_ => PasswordResetsRefreshRequested?.Invoke(this, EventArgs.Empty));
        _eventBus.Subscribe<LeaveRequestUpdatedEvent>(_ => ApprovalHubRefreshRequested?.Invoke(this, EventArgs.Empty));
        _eventBus.Subscribe<ExpenseRequestUpdatedEvent>(_ => ApprovalHubRefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<PairedDeviceEntity> Workstations { get; } = [];
    public ObservableCollection<WorkstationCardItem> WorkstationCards { get; } = [];
    public ObservableCollection<PendingPairingRequest> PendingPairings { get; } = [];
    public ObservableCollection<PasswordResetRequestEntity> PendingPasswordResets { get; } = [];
    public ObservableCollection<AuditLogEntity> RecentAuditLogs { get; } = [];
    public ObservableCollection<AppUsageRecordEntity> TopAppUsages { get; } = [];
    public ObservableCollection<SnapshotTimelineItem> SnapshotTimeline { get; } = [];
    public ObservableCollection<VaultFileEntryDto> SelectedVaultFiles { get; } = [];
    public ObservableCollection<LeaveRequestEntity> PendingLeaveRequests { get; } = [];
    public ObservableCollection<ExpenseRequestEntity> PendingExpenseRequests { get; } = [];
    public ObservableCollection<EmployeeTaskChartRow> EmployeeTaskCharts { get; } = [];
    public ObservableCollection<EmployeeAttendanceChartRow> EmployeeAttendanceCharts { get; } = [];

    public IReadOnlyList<string> AvailableRoles { get; } = CompanyRoles.All;
    public IReadOnlyList<string> AvailableChannels { get; } = ["stable", "beta"];

    // KPI Cards
    [ObservableProperty] private string _kpiOnlineText = "0 / 0";
    [ObservableProperty] private string _kpiActiveHoursText = "0.0 giờ";
    [ObservableProperty] private string _kpiStorageText = "0.0 MB";
    [ObservableProperty] private string _kpiRedAlertsText = "0";
    [ObservableProperty] private string _kpiAlertDetails = "Không có cảnh báo";

    // Approval Hub
    [ObservableProperty] private LeaveRequestEntity? _selectedPendingLeave;
    [ObservableProperty] private ExpenseRequestEntity? _selectedPendingExpense;
    [ObservableProperty] private string _approvalReviewNotes = string.Empty;
    [ObservableProperty] private int _pendingLeaveCount;
    [ObservableProperty] private int _pendingExpenseCount;
    [ObservableProperty] private string _approvalHubSummaryText = "Không có đơn chờ duyệt";

    // Auto-Backup Config
    [ObservableProperty] private bool _autoBackupEnabled;
    [ObservableProperty] private int _autoBackupIntervalHours = 24;
    [ObservableProperty] private int _autoBackupRetentionCount = 7;
    [ObservableProperty] private string _autoBackupLastSummary = "Chưa chạy sao lưu tự động.";

    // Selected Workstation & Actions (Multi-role support)
    [ObservableProperty] private PairedDeviceEntity? _selectedWorkstation;
    [ObservableProperty] private string _workstationSearchQuery = string.Empty;
    private bool _suppressCardCallbacks;
    [ObservableProperty] private bool _editRoleDirector;
    [ObservableProperty] private bool _editRoleAccounting;
    [ObservableProperty] private bool _editRoleLegal;
    [ObservableProperty] private bool _editRoleHr;
    [ObservableProperty] private bool _editRoleStaff = true;
    [ObservableProperty] private bool _editAllowAccounting;
    [ObservableProperty] private bool _editAllowLegal;
    [ObservableProperty] private bool _editAllowOffice = true;
    [ObservableProperty] private string _noticeTitle = "Thông báo nội bộ";
    [ObservableProperty] private string _noticeMessage = string.Empty;
    [ObservableProperty] private VaultFileEntryDto? _selectedVaultFile;
    [ObservableProperty] private string? _lastBackupFilePath;

    // Pairing Center (Multi-role support)
    [ObservableProperty] private bool _isPairingOpen;
    [ObservableProperty] private string _pairingToggleText = "Mở ghép đôi máy mới (tự đóng sau 5 phút)";
    [ObservableProperty] private string _pairingCountdownText = "Đang đóng";
    [ObservableProperty] private PendingPairingRequest? _selectedPairingRequest;
    [ObservableProperty] private PasswordResetRequestEntity? _selectedPasswordReset;
    [ObservableProperty] private bool _approveRoleDirector;
    [ObservableProperty] private bool _approveRoleAccounting;
    [ObservableProperty] private bool _approveRoleLegal;
    [ObservableProperty] private bool _approveRoleHr;
    [ObservableProperty] private bool _approveRoleStaff = true;
    [ObservableProperty] private bool _approveAccounting;
    [ObservableProperty] private bool _approveLegal;
    [ObservableProperty] private bool _approveOffice = true;

    // Release Governance
    [ObservableProperty] private string _fleetMaxApprovedVersion = "0.6.0";
    [ObservableProperty] private string _fleetAllowedChannel = "stable";

    // Snapshot Viewer
    [ObservableProperty] private SnapshotTimelineItem? _selectedSnapshot;
    [ObservableProperty] private BitmapImage? _fullPreviewImage;
    private byte[]? _currentFullJpeg;

    [ObservableProperty] private string _statusBanner = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event EventHandler<string>? InspectDatabaseRequested;
    public event EventHandler? PasswordResetsRefreshRequested;
    public event EventHandler? ApprovalHubRefreshRequested;
    public event EventHandler<SnapshotViewerViewModel>? SnapshotViewerRequested;

    partial void OnWorkstationSearchQueryChanged(string value)
    {
        string? expandedId = WorkstationCards.FirstOrDefault(c => c.IsExpanded)?.MachineId ?? SelectedWorkstation?.MachineId;
        RebuildWorkstationCards(expandedId);
    }

    partial void OnSelectedSnapshotChanged(SnapshotTimelineItem? value)
    {
        _currentFullJpeg = null;
        FullPreviewImage = value?.ThumbnailImage;
        if (value is not null)
        {
            _ = ViewFullSnapshotAsync();
        }
    }

    partial void OnApproveRoleDirectorChanged(bool value) => SyncApproveModulesFromRoles();
    partial void OnApproveRoleAccountingChanged(bool value) => SyncApproveModulesFromRoles();
    partial void OnApproveRoleLegalChanged(bool value) => SyncApproveModulesFromRoles();
    partial void OnApproveRoleHrChanged(bool value) => SyncApproveModulesFromRoles();
    partial void OnApproveRoleStaffChanged(bool value) => SyncApproveModulesFromRoles();

    partial void OnEditRoleDirectorChanged(bool value) => SyncEditModulesFromRoles();
    partial void OnEditRoleAccountingChanged(bool value) => SyncEditModulesFromRoles();
    partial void OnEditRoleLegalChanged(bool value) => SyncEditModulesFromRoles();
    partial void OnEditRoleHrChanged(bool value) => SyncEditModulesFromRoles();
    partial void OnEditRoleStaffChanged(bool value) => SyncEditModulesFromRoles();

    partial void OnSelectedWorkstationChanged(PairedDeviceEntity? value)
    {
        if (value is null)
        {
            return;
        }

        EditRoleDirector = CompanyRoles.HasRole(value.Role, CompanyRoles.Director);
        EditRoleAccounting = CompanyRoles.HasRole(value.Role, CompanyRoles.Accounting);
        EditRoleLegal = CompanyRoles.HasRole(value.Role, CompanyRoles.Legal);
        EditRoleHr = CompanyRoles.HasRole(value.Role, CompanyRoles.Hr);
        EditRoleStaff = CompanyRoles.HasRole(value.Role, CompanyRoles.Staff);
        SyncEditModulesFromRoles();
    }

    private void SyncApproveModulesFromRoles()
    {
        string roleStr = BuildRoleString(ApproveRoleDirector, ApproveRoleAccounting, ApproveRoleLegal, ApproveRoleHr, ApproveRoleStaff);
        IReadOnlyList<string> defaults = CompanyRoles.GetDefaultModulesForRole(roleStr);
        ApproveAccounting = defaults.Contains(ModuleIds.AccountingInvoiceXml);
        ApproveLegal = defaults.Contains(ModuleIds.LegalContractDiff);
        ApproveOffice = defaults.Contains(ModuleIds.OfficePdfStudio);
    }

    private void SyncEditModulesFromRoles()
    {
        string roleStr = BuildRoleString(EditRoleDirector, EditRoleAccounting, EditRoleLegal, EditRoleHr, EditRoleStaff);
        IReadOnlyList<string> defaults = CompanyRoles.GetDefaultModulesForRole(roleStr);
        EditAllowAccounting = defaults.Contains(ModuleIds.AccountingInvoiceXml);
        EditAllowLegal = defaults.Contains(ModuleIds.LegalContractDiff);
        EditAllowOffice = defaults.Contains(ModuleIds.OfficePdfStudio);
    }

    private static string BuildRoleString(bool dir, bool acc, bool leg, bool hr, bool staff)
    {
        var list = new List<string>();
        if (dir) list.Add(CompanyRoles.Director);
        if (acc) list.Add(CompanyRoles.Accounting);
        if (leg) list.Add(CompanyRoles.Legal);
        if (hr) list.Add(CompanyRoles.Hr);
        if (staff) list.Add(CompanyRoles.Staff);
        return CompanyRoles.FormatRoleStorage(list);
    }

    public void RefreshPairingCountdown()
    {
        IsPairingOpen = _pairingManager.IsPairingOpen;
        if (IsPairingOpen)
        {
            TimeSpan rem = _pairingManager.RemainingWindow;
            PairingToggleText = "Đóng cổng ghép đôi ngay";
            PairingCountdownText = $"Đang mở ({rem:mm\\:ss})";
        }
        else
        {
            PairingToggleText = "Mở ghép đôi máy mới (tự đóng sau 5 phút)";
            PairingCountdownText = "Đang đóng";
        }
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IsBusy = true;
        try
        {
            await _fleetSync.RunOnceAsync();
            await _telemetryPull.PullAllOnlinePeersAsync();

            await using AppDbContext appDb = await _appDbFactory.CreateDbContextAsync();
            List<PairedDeviceEntity> devices = await appDb.PairedDevices
                .AsNoTracking()
                .OrderBy(d => d.Role)
                .ThenBy(d => d.FullName)
                .ToListAsync();

            string? expandedId = WorkstationCards.FirstOrDefault(c => c.IsExpanded)?.MachineId;
            string? prevMachineId = expandedId ?? SelectedWorkstation?.MachineId;
            Workstations.Clear();
            foreach (PairedDeviceEntity d in devices)
            {
                Workstations.Add(d);
            }

            RebuildWorkstationCards(expandedId);
            SelectedWorkstation = (expandedId is not null
                                      ? Workstations.FirstOrDefault(w => string.Equals(w.MachineId, expandedId, StringComparison.OrdinalIgnoreCase))
                                      : null)
                                  ?? Workstations.FirstOrDefault(w => string.Equals(w.MachineId, prevMachineId, StringComparison.OrdinalIgnoreCase))
                                  ?? Workstations.FirstOrDefault();

            int onlineCount = devices.Count(d => d.IsOnline);
            KpiOnlineText = $"{onlineCount} / {devices.Count}";

            long totalBytes = devices.Sum(d => d.DbSizeBytes + d.VaultSizeBytes);
            KpiStorageText = $"{totalBytes / (1024.0 * 1024.0):F2} MB";

            List<string> alerts = [];
            foreach (PairedDeviceEntity d in devices)
            {
                if (d.FreeDiskPercent > 0 && d.FreeDiskPercent < 15.0)
                {
                    alerts.Add($"{d.Username}: Ổ cứng thấp ({d.FreeDiskPercent:F1}%)");
                }
            }

            List<AuditLogEntity> audits = await appDb.AuditLogs
                .AsNoTracking()
                .OrderByDescending(a => a.TimestampUtc)
                .Take(80)
                .ToListAsync();

            RecentAuditLogs.Clear();
            foreach (AuditLogEntity a in audits)
            {
                RecentAuditLogs.Add(a);
                if (a.Severity == "Warning" && a.TimestampUtc > DateTimeOffset.UtcNow.AddHours(-24))
                {
                    alerts.Add($"{a.Username}: {a.Action}");
                }
            }

            KpiRedAlertsText = alerts.Count.ToString();
            KpiAlertDetails = alerts.Count == 0 ? "Hệ thống ổn định" : string.Join("; ", alerts.Take(4));

            // Telemetry KPI + Usage + Snapshot Timeline (from Director Cache in telemetry.db)
            await using TelemetryDbContext telDb = await _telDbFactory.CreateDbContextAsync();
            string todayKey = DateTimeOffset.Now.ToString("yyyy-MM-dd");
            long activeSecondsToday = (await telDb.PresenceSessions
                .AsNoTracking()
                .Where(p => p.DateKey == todayKey && p.State == "Active")
                .ToListAsync())
                .Sum(p => p.DurationSeconds);
            KpiActiveHoursText = $"{activeSecondsToday / 3600.0:F1} giờ";

            List<AppUsageRecordEntity> usages = await telDb.AppUsageRecords
                .AsNoTracking()
                .OrderByDescending(u => u.ActiveSeconds)
                .Take(50)
                .ToListAsync();
            TopAppUsages.Clear();
            foreach (AppUsageRecordEntity u in usages)
            {
                TopAppUsages.Add(u);
            }

            List<EncryptedSnapshotEntity> snaps = await telDb.EncryptedSnapshots
                .AsNoTracking()
                .OrderByDescending(s => s.CapturedAtUtc)
                .Take(30)
                .ToListAsync();

            SnapshotTimeline.Clear();
            foreach (EncryptedSnapshotEntity s in snaps)
            {
                BitmapImage? thumbImg = null;
                try
                {
                    if (s.ThumbnailEncryptedBlob.Length > 0)
                    {
                        byte[] jpeg = _telemetryPull.DecryptBlobWithDirectorKey(s.ThumbnailEncryptedBlob);
                        thumbImg = DecodeJpegToBitmapImage(jpeg);
                    }
                }
                catch
                {
                }

                SnapshotTimeline.Add(new SnapshotTimelineItem
                {
                    Id = s.Id,
                    MachineId = s.MachineId,
                    Username = s.Username,
                    CapturedAtUtc = s.CapturedAtUtc,
                    Caption = $"{s.Username} • {s.CapturedAtUtc.ToLocalTime():HH:mm dd/MM}",
                    ThumbnailImage = thumbImg
                });
            }

            RefreshPendingPairingsList();
            RefreshPairingCountdown();
            await RefreshPendingPasswordResetsAsync();
            await RefreshApprovalHubAsync();
            await RefreshVisualChartsAsync(appDb);
            AutoBackupLastSummary = _autoBackup.GetConfig().LastRunSummary;
        }
        catch (Exception ex)
        {
            StatusBanner = $"Lỗi làm mới dữ liệu: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RefreshPendingPairingsList()
    {
        Guid? selectedId = SelectedPairingRequest?.RequestId;
        PendingPairings.Clear();
        foreach (PendingPairingRequest req in _pairingManager.GetPendingRequests())
        {
            PendingPairings.Add(req);
        }

        SelectedPairingRequest = PendingPairings.FirstOrDefault(p => p.RequestId == selectedId)
                                 ?? PendingPairings.FirstOrDefault();
    }

    [RelayCommand]
    private void TogglePairingWindow()
    {
        if (_pairingManager.IsPairingOpen)
        {
            _pairingManager.ClosePairingWindow();
        }
        else
        {
            _pairingManager.OpenPairingWindow(TimeSpan.FromMinutes(5));
        }

        RefreshPairingCountdown();
    }

    [RelayCommand]
    private async Task ApproveSelectedPairingAsync()
    {
        if (SelectedPairingRequest is null)
        {
            StatusBanner = "Chưa chọn yêu cầu ghép đôi cần duyệt.";
            return;
        }

        string roleCsv = BuildRoleString(ApproveRoleDirector, ApproveRoleAccounting, ApproveRoleLegal, ApproveRoleHr, ApproveRoleStaff);
        List<string> modules = BuildModuleSelection(ApproveAccounting, ApproveLegal, ApproveOffice);
        await _pairingManager.ApproveRequestAsync(
            SelectedPairingRequest.RequestId,
            roleCsv,
            modules,
            FleetMaxApprovedVersion,
            FleetAllowedChannel,
            _session.FullName);

        StatusBanner = $"Đã duyệt máy {SelectedPairingRequest.FullName} (Vai trò: {CompanyRoles.FormatRolesBilingual(roleCsv)}, SAS {SelectedPairingRequest.SasCode}).";
        await RefreshAllAsync();
    }

    [RelayCommand]
    private void RejectSelectedPairing()
    {
        if (SelectedPairingRequest is null)
        {
            return;
        }

        _pairingManager.RejectRequest(SelectedPairingRequest.RequestId);
        StatusBanner = $"Đã từ chối yêu cầu của {SelectedPairingRequest.FullName}.";
        RefreshPendingPairingsList();
    }

    [RelayCommand]
    public async Task RefreshPendingPasswordResetsAsync()
    {
        Guid? selectedId = SelectedPasswordReset?.RequestId;
        IReadOnlyList<PasswordResetRequestEntity> list = await _recoveryManager.GetPendingRequestsAsync();
        PendingPasswordResets.Clear();
        foreach (PasswordResetRequestEntity item in list)
        {
            PendingPasswordResets.Add(item);
        }

        SelectedPasswordReset = PendingPasswordResets.FirstOrDefault(x => x.RequestId == selectedId)
                                ?? PendingPasswordResets.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ApproveSelectedPasswordResetAsync()
    {
        if (SelectedPasswordReset is null)
        {
            StatusBanner = "Chưa chọn yêu cầu cấp lại mật khẩu cần duyệt.";
            return;
        }

        await _recoveryManager.ApproveRequestAsync(SelectedPasswordReset.RequestId, _session.FullName);
        StatusBanner = $"Đã cấp vé reset mật khẩu ký số cho {SelectedPasswordReset.FullName} (@{SelectedPasswordReset.Username}).";
        await RefreshPendingPasswordResetsAsync();
    }

    [RelayCommand]
    private async Task RejectSelectedPasswordResetAsync()
    {
        if (SelectedPasswordReset is null)
        {
            return;
        }

        await _recoveryManager.RejectRequestAsync(SelectedPasswordReset.RequestId, _session.FullName);
        StatusBanner = $"Đã từ chối yêu cầu cấp lại mật khẩu của {SelectedPasswordReset.FullName}.";
        await RefreshPendingPasswordResetsAsync();
    }

    private void RebuildWorkstationCards(string? expandedMachineId)
    {
        string q = ChatWorkspaceViewModel.NormalizeForSearch(WorkstationSearchQuery);
        IEnumerable<PairedDeviceEntity> filtered = string.IsNullOrEmpty(q)
            ? Workstations
            : Workstations.Where(d =>
                ChatWorkspaceViewModel.NormalizeForSearch(d.FullName).Contains(q, StringComparison.Ordinal) ||
                ChatWorkspaceViewModel.NormalizeForSearch(d.Username).Contains(q, StringComparison.Ordinal) ||
                ChatWorkspaceViewModel.NormalizeForSearch(d.Role).Contains(q, StringComparison.Ordinal) ||
                ChatWorkspaceViewModel.NormalizeForSearch(CompanyRoles.FormatRolesBilingual(d.Role)).Contains(q, StringComparison.Ordinal) ||
                ChatWorkspaceViewModel.NormalizeForSearch(d.MachineId).Contains(q, StringComparison.Ordinal));

        _suppressCardCallbacks = true;
        try
        {
            WorkstationCards.Clear();
            foreach (PairedDeviceEntity d in filtered)
            {
                WorkstationCards.Add(new WorkstationCardItem
                {
                    Entity = d,
                    ExpandedChangedCallback = OnWorkstationCardExpandedChanged,
                    IsExpanded = !string.IsNullOrEmpty(expandedMachineId) &&
                                 string.Equals(d.MachineId, expandedMachineId, StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        finally
        {
            _suppressCardCallbacks = false;
        }
    }

    private void OnWorkstationCardExpandedChanged(WorkstationCardItem card, bool expanded)
    {
        if (_suppressCardCallbacks || !expanded)
        {
            return;
        }

        _suppressCardCallbacks = true;
        try
        {
            foreach (WorkstationCardItem other in WorkstationCards)
            {
                if (!ReferenceEquals(other, card) && other.IsExpanded)
                {
                    other.IsExpanded = false;
                }
            }
        }
        finally
        {
            _suppressCardCallbacks = false;
        }

        if (!string.Equals(SelectedWorkstation?.MachineId, card.MachineId, StringComparison.OrdinalIgnoreCase))
        {
            SelectedVaultFiles.Clear();
            SelectedVaultFile = null;
        }

        SelectedWorkstation = card.Entity;
    }

    [RelayCommand]
    private async Task ApplyPolicyToSelectedWorkstationAsync()
    {
        if (SelectedWorkstation is null)
        {
            StatusBanner = "Vui lòng mở (chọn) một máy trạm trong danh sách.";
            return;
        }

        string roleCsv = BuildRoleString(EditRoleDirector, EditRoleAccounting, EditRoleLegal, EditRoleHr, EditRoleStaff);
        List<string> modules = BuildModuleSelection(EditAllowAccounting, EditAllowLegal, EditAllowOffice);
        (bool pushedImmediately, _) = await _lanClient.IssueAndDispatchPolicyAsync(
            SelectedWorkstation.MachineId,
            roleCsv,
            modules,
            FleetAllowedChannel,
            FleetMaxApprovedVersion,
            _session.FullName);

        StatusBanner = pushedImmediately
            ? $"Đã đẩy phân quyền mới trực tiếp sang {SelectedWorkstation.Username} (cập nhật nóng không cần khởi động lại)."
            : $"Máy {SelectedWorkstation.Username} đang tắt — đã lưu vào Pending Queue để tự đẩy khi Online.";

        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task LoadSelectedVaultTreeAsync()
    {
        if (!TryGetSelectedOnlineEndpoint(out string endpoint))
        {
            return;
        }

        IReadOnlyList<VaultFileEntryDto> files = await _lanClient.GetVaultTreeAsync(endpoint);
        SelectedVaultFiles.Clear();
        foreach (VaultFileEntryDto f in files)
        {
            SelectedVaultFiles.Add(f);
        }

        StatusBanner = $"Đã tải danh sách {files.Count} tệp Vault từ {SelectedWorkstation!.Username}.";
    }

    [RelayCommand]
    private async Task DownloadSelectedVaultFileAsync()
    {
        if (!TryGetSelectedOnlineEndpoint(out string endpoint) || SelectedVaultFile is null)
        {
            StatusBanner = "Hãy chọn một tệp trong danh sách Vault để tải về.";
            return;
        }

        DownloadedArtifact downloaded = await _lanClient.DownloadVaultFileAsync(endpoint, SelectedVaultFile.RelativePath);
        var dialog = new SaveFileDialog
        {
            FileName = downloaded.FileName,
            Title = "Lưu tệp tải từ Vault LAN"
        };

        if (dialog.ShowDialog() == true)
        {
            await File.WriteAllBytesAsync(dialog.FileName, downloaded.Bytes);
            StatusBanner = $"Đã tải và kiểm chứng SHA-256 ({downloaded.Sha256Header?[..12]}...): {dialog.FileName}";
        }
    }

    [RelayCommand]
    private async Task BackupSelectedDatabaseAsync()
    {
        if (!TryGetSelectedOnlineEndpoint(out string endpoint))
        {
            return;
        }

        IsBusy = true;
        try
        {
            DownloadedArtifact snap = await _lanClient.DownloadDatabaseSnapshotAsync(endpoint);
            string destDir = Path.Combine(_paths.DirectorCacheDirectory, "Snapshots");
            Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(
                destDir,
                $"{SelectedWorkstation!.MachineId}_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.db");

            await File.WriteAllBytesAsync(destPath, snap.Bytes);
            LastBackupFilePath = destPath;

            await using AppDbContext db = await _appDbFactory.CreateDbContextAsync();
            PairedDeviceEntity? tracked = await db.PairedDevices
                .FirstOrDefaultAsync(x => x.MachineId == SelectedWorkstation.MachineId);
            if (tracked is not null)
            {
                tracked.LastBackupUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }

            await _audit.LogAsync("DIRECTOR_BACKUP_DB", "core.director", SelectedWorkstation.MachineId, destPath);
            StatusBanner = $"Đã sao chép nguyên vẹn CSDL (VACUUM INTO, SHA-256 {snap.Sha256Header?[..12]}...) về {destPath}.";
            await RefreshAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenLastBackupInInspector()
    {
        if (string.IsNullOrWhiteSpace(LastBackupFilePath) || !File.Exists(LastBackupFilePath))
        {
            StatusBanner = "Chưa có bản sao lưu DB nào được tải về trong phiên này.";
            return;
        }

        InspectDatabaseRequested?.Invoke(this, LastBackupFilePath);
    }

    [RelayCommand]
    private async Task SendNoticeToSelectedAsync()
    {
        if (!TryGetSelectedOnlineEndpoint(out string endpoint))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(NoticeMessage))
        {
            StatusBanner = "Nhập nội dung thông báo trước khi gửi.";
            return;
        }

        await _lanClient.SendNoticeAsync(endpoint, NoticeTitle, NoticeMessage);
        StatusBanner = $"Đã gửi thông báo LAN tới {SelectedWorkstation!.FullName}.";
        NoticeMessage = string.Empty;
    }

    [RelayCommand]
    private async Task ViewFullSnapshotAsync()
    {
        SnapshotTimelineItem? target = SelectedSnapshot;
        if (target is null)
        {
            StatusBanner = "Chọn một ảnh thumbnail trong dòng thời gian.";
            return;
        }

        try
        {
            byte[] fullJpeg = await _telemetryPull.GetOrFetchDecryptedFullSnapshotAsync(target.Id);
            if (!string.Equals(SelectedSnapshot?.Id.ToString(), target.Id.ToString(), StringComparison.Ordinal))
            {
                return;
            }

            _currentFullJpeg = fullJpeg;
            FullPreviewImage = DecodeJpegToBitmapImage(fullJpeg);
            StatusBanner = $"Đã giải mã ảnh rõ nét 1280px của {target.Username}.";
        }
        catch (Exception ex)
        {
            StatusBanner = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenSnapshotViewer()
    {
        if (SnapshotTimeline.Count == 0)
        {
            StatusBanner = "Chưa có ảnh chụp nào.";
            return;
        }

        int idx = SelectedSnapshot is null ? 0 : SnapshotTimeline.IndexOf(SelectedSnapshot);
        if (idx < 0)
        {
            idx = 0;
        }

        var viewerVm = new SnapshotViewerViewModel(
            SnapshotTimeline.ToArray(),
            idx,
            item => _telemetryPull.GetOrFetchDecryptedFullSnapshotAsync(item.Id),
            Path.Combine(_paths.DirectorCacheDirectory, "SnapshotExports"));

        SnapshotViewerRequested?.Invoke(this, viewerVm);
    }

    [RelayCommand]
    private async Task OpenSnapshotExternallyAsync()
    {
        SnapshotTimelineItem? target = SelectedSnapshot;
        if (target is null)
        {
            StatusBanner = "Chọn một ảnh thumbnail trong dòng thời gian.";
            return;
        }

        try
        {
            byte[] bytes = _currentFullJpeg ?? await _telemetryPull.GetOrFetchDecryptedFullSnapshotAsync(target.Id);
            _currentFullJpeg = bytes;
            string exportDir = Path.Combine(_paths.DirectorCacheDirectory, "SnapshotExports");
            Directory.CreateDirectory(exportDir);
            string destPath = Path.Combine(exportDir, SnapshotViewerViewModel.BuildExportFileName(target));
            await File.WriteAllBytesAsync(destPath, bytes);
            Process.Start(new ProcessStartInfo(destPath)
            {
                UseShellExecute = true
            });
            StatusBanner = $"Đã mở ảnh bằng ứng dụng xem ảnh mặc định: {Path.GetFileName(destPath)}";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Không thể mở ảnh: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSnapshotAsAsync()
    {
        SnapshotTimelineItem? target = SelectedSnapshot;
        if (target is null)
        {
            StatusBanner = "Chọn một ảnh thumbnail trong dòng thời gian.";
            return;
        }

        try
        {
            byte[] bytes = _currentFullJpeg ?? await _telemetryPull.GetOrFetchDecryptedFullSnapshotAsync(target.Id);
            _currentFullJpeg = bytes;
            var dialog = new SaveFileDialog
            {
                Title = "Lưu ảnh chụp màn hình",
                Filter = "Ảnh JPEG (*.jpg)|*.jpg",
                FileName = SnapshotViewerViewModel.BuildExportFileName(target)
            };

            if (dialog.ShowDialog() == true)
            {
                await File.WriteAllBytesAsync(dialog.FileName, bytes);
                StatusBanner = $"Đã lưu ảnh: {dialog.FileName}";
            }
        }
        catch (Exception ex)
        {
            StatusBanner = $"Không thể lưu ảnh: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyReleaseGovernanceFleetWideAsync()
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync();
        List<PairedDeviceEntity> targets = await db.PairedDevices
            .AsNoTracking()
            .Where(x => x.MachineId != _paths.MachineId && x.Role != CompanyRoles.Director)
            .ToListAsync();

        int pushed = 0;
        int queued = 0;
        foreach (PairedDeviceEntity dev in targets)
        {
            IReadOnlyList<string> mods = CompanyRoles.GetDefaultModulesForRole(dev.Role);
            (bool ok, _) = await _lanClient.IssueAndDispatchPolicyAsync(
                dev.MachineId,
                dev.Role,
                mods,
                FleetAllowedChannel,
                FleetMaxApprovedVersion,
                _session.FullName);
            if (ok) pushed++;
            else queued++;
        }

        StatusBanner = $"Đã đặt MaxApprovedVersion={FleetMaxApprovedVersion} ({FleetAllowedChannel}): Đẩy ngay {pushed}, Chờ (Queue) {queued}.";
        await RefreshAllAsync();
    }

    [RelayCommand]
    public async Task RefreshApprovalHubAsync()
    {
        IReadOnlyList<LeaveRequestEntity> leaves = await _leaveService.GetRequestsAsync(pendingOnly: true, mineOnly: false);
        IReadOnlyList<ExpenseRequestEntity> expenses = await _ledgerService.GetExpenseRequestsAsync(pendingOnly: true, mineOnly: false);

        Guid? prevLeaveId = SelectedPendingLeave?.Id;
        PendingLeaveRequests.Clear();
        foreach (LeaveRequestEntity item in leaves)
        {
            PendingLeaveRequests.Add(item);
        }

        SelectedPendingLeave = PendingLeaveRequests.FirstOrDefault(x => x.Id == prevLeaveId) ?? PendingLeaveRequests.FirstOrDefault();

        Guid? prevExpId = SelectedPendingExpense?.Id;
        PendingExpenseRequests.Clear();
        foreach (ExpenseRequestEntity item in expenses)
        {
            PendingExpenseRequests.Add(item);
        }

        SelectedPendingExpense = PendingExpenseRequests.FirstOrDefault(x => x.Id == prevExpId) ?? PendingExpenseRequests.FirstOrDefault();

        PendingLeaveCount = PendingLeaveRequests.Count;
        PendingExpenseCount = PendingExpenseRequests.Count;
        int total = PendingLeaveCount + PendingExpenseCount;
        ApprovalHubSummaryText = total == 0
            ? "Tất cả đơn nghỉ phép / tăng ca và đề xuất chi phí đã được xử lý."
            : $"Đang chờ Giám đốc duyệt: {PendingLeaveCount} đơn nghỉ phép/OT • {PendingExpenseCount} đề xuất chi phí.";
    }

    private async Task RefreshVisualChartsAsync(AppDbContext appDb)
    {
        List<WorkTaskEntity> tasks = await appDb.WorkTasks
            .AsNoTracking()
            .Where(t => !string.IsNullOrWhiteSpace(t.AssigneeFullName))
            .ToListAsync();

        EmployeeTaskCharts.Clear();
        foreach (var g in tasks
                     .GroupBy(t => t.AssigneeFullName)
                     .OrderByDescending(g => g.Count(x => x.Status == WorkTaskStatuses.Completed))
                     .Take(8))
        {
            int total = g.Count();
            int completed = g.Count(x => x.Status == WorkTaskStatuses.Completed);
            double barW = total == 0 ? 4.0 : Math.Max(8.0, (double)completed / total * 220.0);
            int pct = total == 0 ? 0 : (int)Math.Round(completed * 100.0 / total);
            EmployeeTaskCharts.Add(new EmployeeTaskChartRow(
                g.Key,
                completed,
                total,
                $"{completed}/{total} việc ({pct}%)",
                barW));
        }

        string monthPrefix = $"{DateTime.Now:yyyy-MM}-";
        List<AttendanceRecordEntity> monthRecords = await appDb.AttendanceRecords
            .AsNoTracking()
            .Where(r => !r.IsDeleted && r.DateKey.StartsWith(monthPrefix))
            .ToListAsync();

        var attendanceAgg = monthRecords
            .GroupBy(r => string.IsNullOrWhiteSpace(r.EmployeeName) ? r.EmployeeCode : r.EmployeeName)
            .Select(g => new
            {
                Name = g.Key,
                Hours = Math.Round(g.Sum(x => x.WorkedHours), 1),
                LateDays = g.Count(x => x.Status == "Late")
            })
            .OrderByDescending(x => x.Hours)
            .Take(8)
            .ToList();

        double maxHours = attendanceAgg.Count > 0 ? Math.Max(1.0, attendanceAgg.Max(x => x.Hours)) : 1.0;
        EmployeeAttendanceCharts.Clear();
        foreach (var item in attendanceAgg)
        {
            double barW = Math.Max(8.0, item.Hours / maxHours * 220.0);
            EmployeeAttendanceCharts.Add(new EmployeeAttendanceChartRow(
                item.Name,
                item.Hours,
                item.LateDays,
                $"{item.Hours:F1}h • {item.LateDays} muộn",
                barW));
        }
    }

    [RelayCommand]
    private async Task ApproveSelectedLeaveAsync() => await DecideSelectedLeaveAsync(true);

    [RelayCommand]
    private async Task RejectSelectedLeaveAsync() => await DecideSelectedLeaveAsync(false);

    private async Task DecideSelectedLeaveAsync(bool approve)
    {
        if (SelectedPendingLeave is null)
        {
            StatusBanner = "Vui lòng chọn đơn nghỉ phép / tăng ca cần xử lý.";
            return;
        }

        LeaveRequestEntity decided = await _leaveService.DecideRequestAsync(SelectedPendingLeave.Id, approve, ApprovalReviewNotes);
        ApprovalReviewNotes = string.Empty;
        _ = await _collabClient.SyncAllEnterpriseEntitiesWithOnlinePeersAsync();
        StatusBanner = $"Đã {(approve ? "DUYỆT" : "TỪ CHỐI")} đơn {decided.RequestType} của {decided.EmployeeName}.";
        await RefreshAllAsync();
    }

    [RelayCommand]
    private async Task ApproveSelectedExpenseAsync() => await DecideSelectedExpenseAsync(true);

    [RelayCommand]
    private async Task RejectSelectedExpenseAsync() => await DecideSelectedExpenseAsync(false);

    private async Task DecideSelectedExpenseAsync(bool approve)
    {
        if (SelectedPendingExpense is null)
        {
            StatusBanner = "Vui lòng chọn đề xuất chi phí cần xử lý.";
            return;
        }

        ExpenseRequestEntity decided = await _ledgerService.DecideExpenseRequestAsync(SelectedPendingExpense.Id, approve, ApprovalReviewNotes);
        ApprovalReviewNotes = string.Empty;
        _ = await _collabClient.SyncAllEnterpriseEntitiesWithOnlinePeersAsync();
        StatusBanner = $"Đã {(approve ? "DUYỆT" : "TỪ CHỐI")} đề xuất chi phí {decided.AmountVnd:N0}đ của {decided.RequesterFullName}.";
        await RefreshAllAsync();
    }

    [RelayCommand]
    private void SaveAutoBackupConfig()
    {
        DirectorAutoBackupConfig saved = _autoBackup.SaveConfig(
            AutoBackupEnabled,
            AutoBackupIntervalHours,
            AutoBackupRetentionCount);
        AutoBackupEnabled = saved.Enabled;
        AutoBackupIntervalHours = saved.IntervalHours;
        AutoBackupRetentionCount = saved.RetentionCountPerDevice;
        AutoBackupLastSummary = saved.LastRunSummary;
        StatusBanner = saved.Enabled
            ? $"Đã bật Auto-Backup mỗi {saved.IntervalHours}h (giữ {saved.RetentionCountPerDevice} bản/máy)."
            : "Đã lưu cấu hình Auto-Backup (đang tắt).";
    }

    [RelayCommand]
    private async Task RunAutoBackupNowAsync()
    {
        IsBusy = true;
        try
        {
            string summary = await _autoBackup.RunBackupAllOnlineNowAsync();
            AutoBackupLastSummary = summary;
            StatusBanner = summary;
            await RefreshAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryGetSelectedOnlineEndpoint(out string endpoint)
    {
        endpoint = string.Empty;
        if (SelectedWorkstation is null)
        {
            StatusBanner = "Chưa chọn máy trạm.";
            return false;
        }

        if (!SelectedWorkstation.IsOnline ||
            string.IsNullOrWhiteSpace(SelectedWorkstation.LastKnownIp) ||
            SelectedWorkstation.LastKnownPort <= 0)
        {
            StatusBanner = $"Máy {SelectedWorkstation.Username} hiện đang ngoại tuyến.";
            return false;
        }

        endpoint = $"{SelectedWorkstation.LastKnownIp}:{SelectedWorkstation.LastKnownPort}";
        return true;
    }

    private static List<string> BuildModuleSelection(bool accounting, bool legal, bool office)
    {
        var list = new List<string>();
        if (accounting) list.Add(ModuleIds.AccountingInvoiceXml);
        if (legal) list.Add(ModuleIds.LegalContractDiff);
        if (office) list.Add(ModuleIds.OfficePdfStudio);
        return list;
    }

    private static BitmapImage DecodeJpegToBitmapImage(byte[] jpegBytes)
    {
        using var ms = new MemoryStream(jpegBytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
