using System.Collections.ObjectModel;
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

public sealed partial class ExecutiveDashboardViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly IDbContextFactory<TelemetryDbContext> _telDbFactory;
    private readonly PairingSessionManager _pairingManager;
    private readonly AccountRecoveryManager _recoveryManager;
    private readonly DirectorLanClient _lanClient;
    private readonly DirectorFleetSyncService _fleetSync;
    private readonly DirectorTelemetryPullService _telemetryPull;
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
        _paths = paths;
        _session = session;
        _audit = audit;
        _eventBus = eventBus;

        _eventBus.Subscribe<PasswordResetRequestedEvent>(_ => PasswordResetsRefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<PairedDeviceEntity> Workstations { get; } = [];
    public ObservableCollection<PendingPairingRequest> PendingPairings { get; } = [];
    public ObservableCollection<PasswordResetRequestEntity> PendingPasswordResets { get; } = [];
    public ObservableCollection<AuditLogEntity> RecentAuditLogs { get; } = [];
    public ObservableCollection<AppUsageRecordEntity> TopAppUsages { get; } = [];
    public ObservableCollection<SnapshotTimelineItem> SnapshotTimeline { get; } = [];
    public ObservableCollection<VaultFileEntryDto> SelectedVaultFiles { get; } = [];

    public IReadOnlyList<string> AvailableRoles { get; } = CompanyRoles.All;
    public IReadOnlyList<string> AvailableChannels { get; } = ["stable", "beta"];

    // KPI Cards
    [ObservableProperty] private string _kpiOnlineText = "0 / 0";
    [ObservableProperty] private string _kpiActiveHoursText = "0.0 giờ";
    [ObservableProperty] private string _kpiStorageText = "0.0 MB";
    [ObservableProperty] private string _kpiRedAlertsText = "0";
    [ObservableProperty] private string _kpiAlertDetails = "Không có cảnh báo";

    // Selected Workstation & Actions (Multi-role support)
    [ObservableProperty] private PairedDeviceEntity? _selectedWorkstation;
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
    [ObservableProperty] private string _fleetMaxApprovedVersion = "0.4.0";
    [ObservableProperty] private string _fleetAllowedChannel = "stable";

    // Snapshot Viewer
    [ObservableProperty] private SnapshotTimelineItem? _selectedSnapshot;
    [ObservableProperty] private BitmapImage? _fullPreviewImage;

    [ObservableProperty] private string _statusBanner = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public event EventHandler<string>? InspectDatabaseRequested;
    public event EventHandler? PasswordResetsRefreshRequested;

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

            string? prevMachineId = SelectedWorkstation?.MachineId;
            Workstations.Clear();
            foreach (PairedDeviceEntity d in devices)
            {
                Workstations.Add(d);
            }

            SelectedWorkstation = Workstations.FirstOrDefault(w => w.MachineId == prevMachineId)
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

    [RelayCommand]
    private async Task ApplyPolicyToSelectedWorkstationAsync()
    {
        if (SelectedWorkstation is null)
        {
            StatusBanner = "Vui lòng chọn máy trạm trong lưới.";
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
        if (SelectedSnapshot is null)
        {
            StatusBanner = "Chọn một ảnh thumbnail trong dòng thời gian.";
            return;
        }

        try
        {
            byte[] fullJpeg = await _telemetryPull.GetOrFetchDecryptedFullSnapshotAsync(SelectedSnapshot.Id);
            FullPreviewImage = DecodeJpegToBitmapImage(fullJpeg);
            StatusBanner = $"Đã giải mã ảnh rõ nét 1280px của {SelectedSnapshot.Username}.";
        }
        catch (Exception ex)
        {
            StatusBanner = ex.Message;
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
