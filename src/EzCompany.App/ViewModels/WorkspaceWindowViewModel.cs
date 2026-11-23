using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.App.Views;
using EzCompany.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace EzCompany.App.ViewModels;

public sealed partial class WorkspaceWindowViewModel : ObservableObject
{
    private readonly ISessionContext _session;
    private readonly IPermissionService _permissions;
    private readonly ILocalStoragePaths _paths;
    private readonly IEnumerable<IAppModule> _modules;
    private readonly IServiceProvider _services;
    private readonly IEventBus _eventBus;
    private readonly ThemeService _theme;
    private readonly RollingFileLoggerProvider? _fileLogger;
    private readonly ILogger<WorkspaceWindowViewModel> _logger;

    public WorkspaceWindowViewModel(
        ISessionContext session,
        IPermissionService permissions,
        ILocalStoragePaths paths,
        IEnumerable<IAppModule> modules,
        IServiceProvider services,
        IEventBus eventBus,
        ThemeService theme,
        ILogger<WorkspaceWindowViewModel> logger,
        RollingFileLoggerProvider? fileLogger = null)
    {
        _session = session;
        _permissions = permissions;
        _paths = paths;
        _modules = modules;
        _services = services;
        _eventBus = eventBus;
        _theme = theme;
        _logger = logger;
        _fileLogger = fileLogger;

        _permissions.PolicyChanged += (_, _) => PolicyRefreshRequested?.Invoke(this, EventArgs.Empty);
        _eventBus.Subscribe<InternalNoticeEvent>(evt =>
        {
            ActiveNoticeBanner = $"[{evt.SenderAdminName}] {evt.Title}: {evt.Message}";
            NoticeReceived?.Invoke(this, evt);
        });
    }

    public ObservableCollection<NavigationItemModel> NavigationItems { get; } = [];

    [ObservableProperty] private NavigationItemModel? _selectedNavItem;
    [ObservableProperty] private object? _activeView;
    [ObservableProperty] private string _windowTitle = "Ez Company — Workspace";
    [ObservableProperty] private string _companyTitle = string.Empty;
    [ObservableProperty] private string _userSubtitle = string.Empty;
    [ObservableProperty] private string _policySummary = string.Empty;
    [ObservableProperty] private string _storageSummary = string.Empty;
    [ObservableProperty] private string _lanSummary = string.Empty;
    [ObservableProperty] private string _logSummary = string.Empty;
    [ObservableProperty] private bool _isPolicyLocked;
    [ObservableProperty] private string? _policyLockReason;
    [ObservableProperty] private string? _activeNoticeBanner;

    public event EventHandler? PolicyRefreshRequested;
    public event EventHandler? LogoutRequested;
    public event EventHandler<InternalNoticeEvent>? NoticeReceived;

    public void InitializeWorkspace()
    {
        WindowTitle = $"Ez Company — {_session.CompanyName} ({_paths.ProfileName})";
        CompanyTitle = $"{_session.CompanyName} (MST: {_session.TaxCode})";
        UserSubtitle = $"Tài khoản: {_session.FullName} (@{_session.Username})  •  Vai trò: {_session.Role}";
        RebuildNavigation();
    }

    public void RebuildNavigation()
    {
        string? previousKey = SelectedNavItem?.Key;
        PolicySnapshot? policy = _permissions.CurrentPolicy;

        IsPolicyLocked = _permissions.IsLocked;
        PolicyLockReason = _permissions.LockReason;

        PolicySummary = policy is null
            ? "Chưa có policy.lic hợp lệ."
            : $"Policy v{policy.PolicyVersion} • Vai trò: {policy.Role} • Hiệu lực đến: {policy.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} • Kênh: {policy.AllowedUpdateChannel} (MaxApproved: {policy.MaxApprovedVersion})";

        StorageSummary = $"Profile: {_paths.ProfileName} • MachineId: {_paths.MachineId} • Vault: {_paths.VaultDirectory}";
        LanSummary = $"Cổng LAN Kestrel: {(_session.CurrentLanPort > 0 ? _session.CurrentLanPort : "Khởi động...")} • Phiên bản App: {_session.CurrentAppVersion}";
        string logPath = _fileLogger?.CurrentLogFilePath ?? _paths.LogsDirectory;
        LogSummary = $"Tệp nhật ký runtime: {logPath}";

        NavigationItems.Clear();

        NavigationItems.Add(new NavigationItemModel
        {
            Key = "home",
            Title = "Tổng quan Trạm",
            Subtitle = "Trạng thái hồ sơ & giấy phép",
            CreateContent = () => new WorkspaceHomeView { DataContext = this }
        });

        if (_session.IsDirectorNode && !_permissions.IsLocked)
        {
            NavigationItems.Add(new NavigationItemModel
            {
                Key = "director.dashboard",
                Title = "Bảng Tổng quan Giám đốc",
                Subtitle = "LAN, Telemetry, Ghép đôi SAS",
                CreateContent = () =>
                {
                    var view = (ExecutiveDashboardView)_services.GetService(typeof(ExecutiveDashboardView))!;
                    if (view.DataContext is ExecutiveDashboardViewModel vm)
                    {
                        vm.InspectDatabaseRequested += async (_, dbPath) => await OpenDbInspectorAsync(dbPath);
                    }
                    return view;
                }
            });

            NavigationItems.Add(new NavigationItemModel
            {
                Key = "director.db_inspector",
                Title = "Read-Only DB Inspector",
                Subtitle = "Xem bản chụp .db chỉ đọc",
                CreateContent = () => _services.GetService(typeof(DbInspectorView))!
            });
        }

        if (!_permissions.IsLocked)
        {
            foreach (IAppModule module in _modules.OrderBy(m => m.Metadata.DisplayOrder))
            {
                if (_permissions.HasModuleAccess(module.Metadata.ModuleId))
                {
                    IAppModule captured = module;
                    NavigationItems.Add(new NavigationItemModel
                    {
                        Key = captured.Metadata.ModuleId,
                        Title = captured.Metadata.DisplayName,
                        Subtitle = captured.Metadata.Category,
                        CreateContent = () => captured.CreateView(_services)
                    });
                }
            }
        }

        NavigationItems.Add(new NavigationItemModel
        {
            Key = "core.updates",
            Title = "Cập nhật & Bảo trì",
            Subtitle = "Velopack + SafeMigration",
            CreateContent = () => _services.GetService(typeof(UpdatesSettingsView))!
        });

        NavigationItemModel? next = NavigationItems.FirstOrDefault(x => x.Key == previousKey)
                                    ?? NavigationItems.FirstOrDefault();
        SelectedNavItem = next;
    }

    partial void OnSelectedNavItemChanged(NavigationItemModel? value)
    {
        ActiveView = value?.CreateContent();
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.ToggleTheme();

    [RelayCommand]
    private void DismissNotice() => ActiveNoticeBanner = null;

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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open logs folder {LogsDirectory}.", _paths.LogsDirectory);
        }
    }

    public async Task OpenDbInspectorAsync(string dbFilePath)
    {
        NavigationItemModel? item = NavigationItems.FirstOrDefault(x => x.Key == "director.db_inspector");
        if (item is not null)
        {
            SelectedNavItem = item;
            if (ActiveView is DbInspectorView inspectorView && inspectorView.DataContext is DbInspectorViewModel vm)
            {
                await vm.LoadFileAsync(dbFilePath);
            }
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _session.ClearAuthentication();
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }
}
