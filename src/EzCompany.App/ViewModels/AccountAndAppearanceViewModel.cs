using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Tasks;

namespace EzCompany.App.ViewModels;

public sealed record AccentPresetOption(string Name, string Hex);

public sealed partial class AccountAndAppearanceViewModel : ObservableObject
{
    private readonly GenesisBootstrapService _bootstrap;
    private readonly LanTaskClient _taskClient;
    private readonly AppearanceSettingsService _appearance;
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly ILocalStoragePaths _paths;
    private readonly RollingFileLoggerProvider? _fileLogger;

    private bool _suppressLiveApply;

    public AccountAndAppearanceViewModel(
        GenesisBootstrapService bootstrap,
        LanTaskClient taskClient,
        AppearanceSettingsService appearance,
        ISessionContext session,
        IPermissionService permissions,
        ILocalStoragePaths paths,
        RollingFileLoggerProvider? fileLogger = null)
    {
        _bootstrap = bootstrap;
        _taskClient = taskClient;
        _appearance = appearance;
        _session = session;
        _permissions = permissions;
        _paths = paths;
        _fileLogger = fileLogger;

        AccentPresets = AppearanceSettingsService.AccentPresets
            .Select(p => new AccentPresetOption(p.Name, p.Hex))
            .ToArray();
        AvailableBackdrops = AppearanceSettingsService.AvailableBackdrops;

        LoadFromCurrentState();
    }

    public IReadOnlyList<AccentPresetOption> AccentPresets { get; }
    public IReadOnlyList<string> AvailableBackdrops { get; }

    // Account profile & credentials
    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _roleDisplay = string.Empty;
    [ObservableProperty] private string _machineIdDisplay = string.Empty;
    [ObservableProperty] private string _accountStatusMessage = string.Empty;

    // Station diagnostics
    [ObservableProperty] private string _policySummary = string.Empty;
    [ObservableProperty] private string _storageSummary = string.Empty;
    [ObservableProperty] private string _lanSummary = string.Empty;
    [ObservableProperty] private string _logSummary = string.Empty;

    // Appearance controls
    [ObservableProperty] private bool _isDarkMode = true;
    [ObservableProperty] private string _accentColorHex = "#0078D4";
    [ObservableProperty] private double _baseFontSize = 14.0;
    [ObservableProperty] private double _cardMaxWidth = 1080.0;
    [ObservableProperty] private double _cardCornerRadius = 10.0;
    [ObservableProperty] private double _cardPadding = 16.0;
    [ObservableProperty] private double _sidebarWidth = 260.0;
    [ObservableProperty] private double _windowOpacity = 1.0;
    [ObservableProperty] private string _backdropType = "Mica";
    [ObservableProperty] private string _appearanceStatusMessage = string.Empty;

    public void LoadFromCurrentState()
    {
        FullName = _session.FullName;
        Username = _session.Username;
        RoleDisplay = $"{_session.Role} ({(_session.IsDirectorNode ? "Giám đốc" : "Nhân viên")})";
        MachineIdDisplay = _paths.MachineId;

        PolicySnapshot? policy = _permissions.CurrentPolicy;
        PolicySummary = policy is null
            ? "Chưa có policy.lic hợp lệ."
            : $"Policy v{policy.PolicyVersion} • Vai trò: {policy.Role} • Hiệu lực đến: {policy.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} • Kênh: {policy.AllowedUpdateChannel} (MaxApproved: {policy.MaxApprovedVersion})";
        StorageSummary = $"Profile: {_paths.ProfileName} • Vault: {_paths.VaultDirectory}";
        LanSummary = $"Cổng LAN Kestrel: {(_session.CurrentLanPort > 0 ? _session.CurrentLanPort : 0)} • Phiên bản App: {_session.CurrentAppVersion}";
        LogSummary = $"Nhật ký runtime: {_fileLogger?.CurrentLogFilePath ?? _paths.LogsDirectory}";

        SyncAppearanceFields(_appearance.Current);
    }

    partial void OnIsDarkModeChanged(bool value) => PushAppearanceChange();
    partial void OnBaseFontSizeChanged(double value) => PushAppearanceChange();
    partial void OnCardMaxWidthChanged(double value) => PushAppearanceChange();
    partial void OnCardCornerRadiusChanged(double value) => PushAppearanceChange();
    partial void OnCardPaddingChanged(double value) => PushAppearanceChange();
    partial void OnSidebarWidthChanged(double value) => PushAppearanceChange();
    partial void OnWindowOpacityChanged(double value) => PushAppearanceChange();
    partial void OnBackdropTypeChanged(string value) => PushAppearanceChange();

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
    private void ApplyAccentPreset(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return;
        }

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
    private void ResetAppearance()
    {
        AppearanceSettings defaults = _appearance.ResetToDefaults();
        SyncAppearanceFields(defaults);
        AppearanceStatusMessage = "Đã khôi phục cài đặt giao diện mặc định.";
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
            BackdropType = s.BackdropType;
        }
        finally
        {
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
            BackdropType = BackdropType
        });

        AccentColorHex = applied.AccentColorHex;
    }
}
