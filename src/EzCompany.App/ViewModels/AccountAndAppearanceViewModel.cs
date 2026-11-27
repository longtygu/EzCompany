using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Collaboration;
using EzCompany.Core.LanAgent.Tasks;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed record AccentPresetOption(string Name, string Hex);
public sealed record BackdropOption(string Key, string Label);

public sealed partial class AccountAndAppearanceViewModel : ObservableObject
{
    private readonly GenesisBootstrapService _bootstrap;
    private readonly CompanyBrandingService _branding;
    private readonly LanTaskClient _taskClient;
    private readonly LanCollaborationClient _collabClient;
    private readonly AppearanceSettingsService _appearance;
    private readonly AppDiagnosticsAndRepairService _diagnostics;
    private readonly UserAvatarService _avatars;
    private readonly WindowsStartupService? _startup;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly ILocalStoragePaths _paths;
    private readonly RollingFileLoggerProvider? _fileLogger;

    private bool _suppressLiveApply;

    public AccountAndAppearanceViewModel(
        GenesisBootstrapService bootstrap,
        CompanyBrandingService branding,
        LanTaskClient taskClient,
        LanCollaborationClient collabClient,
        AppearanceSettingsService appearance,
        AppDiagnosticsAndRepairService diagnostics,
        UserAvatarService avatars,
        UpdatesSettingsViewModel updatesVm,
        ISessionContext session,
        IPermissionService permissions,
        ILocalStoragePaths paths,
        RollingFileLoggerProvider? fileLogger = null,
        WindowsStartupService? startup = null)
    {
        _bootstrap = bootstrap;
        _branding = branding;
        _taskClient = taskClient;
        _collabClient = collabClient;
        _appearance = appearance;
        _diagnostics = diagnostics;
        _avatars = avatars;
        _startup = startup;
        UpdatesVm = updatesVm;
        _session = session;
        _permissions = permissions;
        _paths = paths;
        _fileLogger = fileLogger;

        AccentPresets = AppearanceSettingsService.AccentPresets
            .Select(p => new AccentPresetOption(p.Name, p.Hex))
            .ToArray();
        BackdropOptions = AppearanceSettingsService.BackdropOptions
            .Select(b => new BackdropOption(b.Key, b.Label))
            .ToArray();
    }

    public UpdatesSettingsViewModel UpdatesVm { get; }
    public IReadOnlyList<AccentPresetOption> AccentPresets { get; }
    public IReadOnlyList<BackdropOption> BackdropOptions { get; }
    public IReadOnlyList<string> LogLevels { get; } = ["ALL", "INF", "WRN", "ERR"];

    public ObservableCollection<string> RecentLogLines { get; } = [];
    public ObservableCollection<DiagnosticRepairItem> DiagnosticResults { get; } = [];

    // Tab 1: Appearance controls
    [ObservableProperty] private bool _isDarkMode = true;
    [ObservableProperty] private string _accentColorHex = "#0078D4";
    [ObservableProperty] private double _baseFontSize = 14.0;
    [ObservableProperty] private double _cardMaxWidth = 960.0;
    [ObservableProperty] private double _cardCornerRadius = 10.0;
    [ObservableProperty] private double _cardPadding = 16.0;
    [ObservableProperty] private double _sidebarWidth = 260.0;
    [ObservableProperty] private double _windowOpacity = 1.0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowOpacityPercent))]
    private int _windowTransparencyPercent;
    [ObservableProperty] private string _windowTransparencyInput = "0";
    [ObservableProperty] private string _backdropType = "None";
    [ObservableProperty] private double _cardOpacity = 1.0;
    [ObservableProperty] private int _cardTransparencyPercent;
    [ObservableProperty] private double _cardTintStrength = 0.08;
    [ObservableProperty] private bool _minimizeToTrayOnClose = true;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string _appearanceStatusMessage = string.Empty;

    public int WindowOpacityPercent => 100 - WindowTransparencyPercent;
    private bool _syncingPercent;

    // Tab 2: Account, Avatar & Company Logo
    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _roleDisplay = string.Empty;
    [ObservableProperty] private string _machineIdDisplay = string.Empty;
    [ObservableProperty] private bool _isDirectorNode;
    [ObservableProperty] private BitmapImage? _companyLogoPreview;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPersonalAvatar))]
    private BitmapImage? _personalAvatarPreview;
    public bool HasPersonalAvatar => PersonalAvatarPreview is not null;
    [ObservableProperty] private string _accountStatusMessage = string.Empty;
    [ObservableProperty] private string _policySummary = string.Empty;
    [ObservableProperty] private string _storageSummary = string.Empty;
    [ObservableProperty] private string _lanSummary = string.Empty;
    [ObservableProperty] private string _logSummary = string.Empty;

    // Tab 4: Bug report, Log viewer, Self-repair
    [ObservableProperty] private string _bugReportTitle = string.Empty;
    [ObservableProperty] private string _bugReportDescription = string.Empty;
    [ObservableProperty] private string _selectedLogLevel = "ALL";
    [ObservableProperty] private string _diagnosticsStatusMessage = string.Empty;

    public async Task LoadAllAsync()
    {
        FullName = _session.FullName;
        Username = _session.Username;
        IsDirectorNode = _session.IsDirectorNode;
        RoleDisplay = $"{CompanyRoles.FormatRolesBilingual(_session.Role)} • MachineId: {_paths.MachineId}";
        MachineIdDisplay = _paths.MachineId;

        PolicySnapshot? policy = _permissions.CurrentPolicy;
        PolicySummary = policy is null
            ? "Chưa có policy.lic hợp lệ."
            : $"Policy v{policy.PolicyVersion} • Chức danh: {CompanyRoles.FormatRolesBilingual(policy.Role)} • Hiệu lực đến: {policy.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} • MaxApproved: {policy.MaxApprovedVersion}";
        StorageSummary = $"Profile: {_paths.ProfileName} • Vault: {_paths.VaultDirectory}";
        LanSummary = $"Cổng LAN Kestrel: {(_session.CurrentLanPort > 0 ? _session.CurrentLanPort : 0)} • Phiên bản App: {_session.CurrentAppVersion}";
        LogSummary = $"Nhật ký runtime: {_fileLogger?.CurrentLogFilePath ?? _paths.LogsDirectory}";

        SyncAppearanceFields(_appearance.Current);
        UpdatesVm.Load();

        byte[]? logoBytes = await _branding.GetCompanyLogoBytesAsync();
        CompanyLogoPreview = CompanyLogoImageHelper.LoadFromBytesOrDefault(logoBytes);

        byte[]? avatarBytes = await _avatars.GetMyAvatarBytesAsync();
        PersonalAvatarPreview = avatarBytes is { Length: > 0 } ? LoadBitmapFrozen(avatarBytes) : null;

        await ReloadLogsAsync();
    }

    partial void OnIsDarkModeChanged(bool value) => PushAppearanceChange();
    partial void OnBaseFontSizeChanged(double value) => PushAppearanceChange();
    partial void OnCardMaxWidthChanged(double value) => PushAppearanceChange();
    partial void OnCardCornerRadiusChanged(double value) => PushAppearanceChange();
    partial void OnCardPaddingChanged(double value) => PushAppearanceChange();
    partial void OnSidebarWidthChanged(double value) => PushAppearanceChange();
    partial void OnWindowOpacityChanged(double value)
    {
        if (!_syncingPercent)
        {
            _syncingPercent = true;
            int pct = Math.Clamp((int)Math.Round((1.0 - value) * 100.0), 0, 70);
            WindowTransparencyPercent = pct;
            WindowTransparencyInput = pct.ToString();
            _syncingPercent = false;
        }

        PushAppearanceChange();
    }

    partial void OnWindowTransparencyPercentChanged(int value)
    {
        if (_syncingPercent)
        {
            return;
        }

        _syncingPercent = true;
        int clamped = Math.Clamp(value, 0, 70);
        if (clamped != value)
        {
            WindowTransparencyPercent = clamped;
        }

        WindowTransparencyInput = clamped.ToString();
        WindowOpacity = Math.Round(1.0 - clamped / 100.0, 2);
        _syncingPercent = false;
        PushAppearanceChange();
    }

    partial void OnCardOpacityChanged(double value)
    {
        if (!_syncingPercent)
        {
            _syncingPercent = true;
            CardTransparencyPercent = Math.Clamp((int)Math.Round((1.0 - value) * 100.0), 0, 60);
            _syncingPercent = false;
        }

        PushAppearanceChange();
    }

    partial void OnCardTransparencyPercentChanged(int value)
    {
        if (_syncingPercent)
        {
            return;
        }

        _syncingPercent = true;
        int clamped = Math.Clamp(value, 0, 60);
        if (clamped != value)
        {
            CardTransparencyPercent = clamped;
        }

        CardOpacity = Math.Round(1.0 - clamped / 100.0, 2);
        _syncingPercent = false;
        PushAppearanceChange();
    }

    partial void OnBackdropTypeChanged(string value) => PushAppearanceChange();
    partial void OnCardTintStrengthChanged(double value) => PushAppearanceChange();
    partial void OnMinimizeToTrayOnCloseChanged(bool value) => PushAppearanceChange();
    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_suppressLiveApply && _startup is not null)
        {
            (bool ok, string msg) = _startup.SetEnabled(value);
            if (!ok)
            {
                AppearanceStatusMessage = msg;
            }
        }

        PushAppearanceChange();
    }

    partial void OnSelectedLogLevelChanged(string value) => _ = ReloadLogsAsync();

    public async Task ChangeCredentialsAsync(string currentPassword, string newPassword, string confirmNewPassword)
    {
        AccountStatusMessage = string.Empty;
        if (string.IsNullOrEmpty(currentPassword))
        {
            AccountStatusMessage = "Vui lòng nhập mật khẩu hiện tại để xác nhận thay đổi.";
            return;
        }

        if (!string.IsNullOrEmpty(newPassword) && newPassword != confirmNewPassword)
        {
            AccountStatusMessage = "Mật khẩu mới và mật khẩu xác nhận không khớp.";
            return;
        }

        try
        {
            LocalUserEntity updated = await _bootstrap.ChangeCredentialsAsync(
                _session.UserId,
                currentPassword,
                FullName,
                Username,
                string.IsNullOrEmpty(newPassword) ? null : newPassword);

            if (!_session.IsDirectorNode)
            {
                await _taskClient.NotifyProfileUpdateToDirectorsAsync(updated.Username, updated.FullName);
            }

            FullName = updated.FullName;
            Username = updated.Username;
            AccountStatusMessage = string.IsNullOrEmpty(newPassword)
                ? "Đã cập nhật tên hiển thị và tên đăng nhập thành công."
                : "Đã cập nhật thông tin tài khoản và mật khẩu mới thành công.";
        }
        catch (Exception ex)
        {
            AccountStatusMessage = $"Không thể cập nhật tài khoản: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UploadCompanyLogoAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn ảnh Logo Công ty (PNG / JPG, tối đa 2 MB)",
            Filter = "Hình ảnh Logo (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _branding.SaveCompanyLogoFromFileAsync(dialog.FileName);
            byte[]? bytes = await _branding.GetCompanyLogoBytesAsync();
            CompanyLogoPreview = CompanyLogoImageHelper.LoadFromBytesOrDefault(bytes);

            int pushed = IsDirectorNode ? await _collabClient.PushCompanyBrandingToOnlinePeersAsync() : 0;
            AccountStatusMessage = IsDirectorNode
                ? $"Đã cập nhật Logo Công ty và đồng bộ tới {pushed} máy trạm đang Online (máy khác sẽ tự kéo khi kết nối)."
                : "Đã cập nhật Logo Công ty cục bộ.";
        }
        catch (Exception ex)
        {
            AccountStatusMessage = $"Lỗi cập nhật logo: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveCompanyLogoAsync()
    {
        await _branding.RemoveCompanyLogoAsync();
        CompanyLogoPreview = CompanyLogoImageHelper.LoadFromBytesOrDefault(null);
        if (IsDirectorNode)
        {
            _ = await _collabClient.PushCompanyBrandingToOnlinePeersAsync();
        }
        AccountStatusMessage = "Đã khôi phục Logo mặc định của Ez Company.";
    }

    [RelayCommand]
    private async Task UploadPersonalAvatarAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn Ảnh đại diện cá nhân (PNG / JPG)",
            Filter = "Hình ảnh (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            byte[] encoded = ResizeImageToPngBytes(dialog.FileName, 256);
            await _avatars.SaveMyAvatarAsync(encoded);
            PersonalAvatarPreview = LoadBitmapFrozen(encoded);
            _ = await _collabClient.SyncAllEnterpriseEntitiesWithOnlinePeersAsync();
            AccountStatusMessage = "Đã cập nhật ảnh đại diện cá nhân và đồng bộ tới các máy LAN.";
        }
        catch (Exception ex)
        {
            AccountStatusMessage = $"Lỗi cập nhật ảnh đại diện: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemovePersonalAvatarAsync()
    {
        await _avatars.ClearMyAvatarAsync();
        PersonalAvatarPreview = null;
        _ = await _collabClient.SyncAllEnterpriseEntitiesWithOnlinePeersAsync();
        AccountStatusMessage = "Đã xóa ảnh đại diện cá nhân.";
    }

    private static byte[] ResizeImageToPngBytes(string filePath, int maxDimension)
    {
        var src = new BitmapImage();
        src.BeginInit();
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.UriSource = new Uri(filePath, UriKind.Absolute);
        src.EndInit();

        BitmapSource processed = src;
        if (src.PixelWidth > maxDimension || src.PixelHeight > maxDimension)
        {
            double scale = Math.Min((double)maxDimension / src.PixelWidth, (double)maxDimension / src.PixelHeight);
            processed = new TransformedBitmap(src, new ScaleTransform(scale, scale));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(processed));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static BitmapImage LoadBitmapFrozen(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    [RelayCommand]
    private void ApplyAccentPreset(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        AccentColorHex = hex;
        PushAppearanceChange();
        AppearanceStatusMessage = $"Đã áp dụng màu chủ đề {AccentColorHex}.";
    }

    [RelayCommand]
    private void ApplyCustomAccent()
    {
        PushAppearanceChange();
        AppearanceStatusMessage = $"Đã áp dụng màu chủ đề {AccentColorHex}.";
    }

    [RelayCommand]
    private void SetWindowTransparencyPreset(string? percentText)
    {
        if (int.TryParse(percentText, out int pct))
        {
            WindowTransparencyPercent = Math.Clamp(pct, 0, 70);
        }
    }

    [RelayCommand]
    private void ApplyWindowTransparencyInput()
    {
        if (int.TryParse(WindowTransparencyInput?.Trim().TrimEnd('%'), out int pct))
        {
            WindowTransparencyPercent = Math.Clamp(pct, 0, 70);
        }
        else
        {
            AppearanceStatusMessage = "Vui lòng nhập số % độ trong suốt hợp lệ (từ 0 đến 70).";
        }
    }

    [RelayCommand]
    private void ResetAppearance()
    {
        AppearanceSettings defaults = _appearance.ResetToDefaults();
        SyncAppearanceFields(defaults);
        AppearanceStatusMessage = "Đã khôi phục cài đặt giao diện mặc định.";
    }

    [RelayCommand]
    private async Task ReloadLogsAsync()
    {
        IReadOnlyList<string> lines = await _diagnostics.LoadRecentLogLinesAsync(SelectedLogLevel, 250);
        RecentLogLines.Clear();
        foreach (string line in lines)
        {
            RecentLogLines.Add(line);
        }
    }

    [RelayCommand]
    private async Task ExportBugReportAsync()
    {
        try
        {
            string zipPath = await _diagnostics.CreateBugReportZipAsync(BugReportTitle, BugReportDescription);
            BugReportTitle = string.Empty;
            BugReportDescription = string.Empty;
            DiagnosticsStatusMessage = $"Đã đóng gói báo lỗi kèm nhật ký log an toàn tại: {zipPath}";
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zipPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagnosticsStatusMessage = $"Không thể tạo báo lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunAutoScanAndRepairAsync()
    {
        IReadOnlyList<DiagnosticRepairItem> items = await _diagnostics.RunAutoScanAndRepairAsync(repair: true);
        DiagnosticResults.Clear();
        foreach (DiagnosticRepairItem item in items)
        {
            DiagnosticResults.Add(item);
        }

        DiagnosticsStatusMessage = $"Đã hoàn tất quét & tự sửa lỗi ({items.Count} hạng mục kiểm tra).";
        await ReloadLogsAsync();
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", _paths.LogsDirectory)
            {
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void SyncAppearanceFields(AppearanceSettings s)
    {
        _suppressLiveApply = true;
        _syncingPercent = true;
        try
        {
            IsDarkMode = s.IsDarkMode;
            AccentColorHex = s.AccentColorHex;
            BaseFontSize = s.BaseFontSize;
            CardMaxWidth = s.CardMaxWidth;
            CardCornerRadius = s.CardCornerRadius;
            CardPadding = s.CardPadding;
            SidebarWidth = s.SidebarWidth;
            WindowOpacity = s.WindowOpacity;
            int winPct = Math.Clamp((int)Math.Round((1.0 - s.WindowOpacity) * 100.0), 0, 70);
            WindowTransparencyPercent = winPct;
            WindowTransparencyInput = winPct.ToString();
            BackdropType = s.BackdropType;
            CardOpacity = s.CardOpacity;
            CardTransparencyPercent = Math.Clamp((int)Math.Round((1.0 - s.CardOpacity) * 100.0), 0, 60);
            CardTintStrength = s.CardTintStrength;
            MinimizeToTrayOnClose = s.MinimizeToTrayOnClose;
            StartWithWindows = (_startup?.IsEnabled() ?? false) || s.StartWithWindows;
        }
        finally
        {
            _syncingPercent = false;
            _suppressLiveApply = false;
        }
    }

    private void PushAppearanceChange()
    {
        if (_suppressLiveApply)
        {
            return;
        }

        AppearanceSettings applied = _appearance.Apply(new AppearanceSettings
        {
            IsDarkMode = IsDarkMode,
            AccentColorHex = AccentColorHex,
            BaseFontSize = BaseFontSize,
            CardMaxWidth = CardMaxWidth,
            CardCornerRadius = CardCornerRadius,
            CardPadding = CardPadding,
            SidebarWidth = SidebarWidth,
            WindowOpacity = WindowOpacity,
            BackdropType = BackdropType,
            CardOpacity = CardOpacity,
            CardTintStrength = CardTintStrength,
            MinimizeToTrayOnClose = MinimizeToTrayOnClose,
            StartWithWindows = StartWithWindows
        });

        AccentColorHex = applied.AccentColorHex;
        AppearanceStatusMessage = $"Đã áp dụng trực tiếp. Chất liệu thực tế: {_appearance.LastEffectiveMaterial}";
    }
}
