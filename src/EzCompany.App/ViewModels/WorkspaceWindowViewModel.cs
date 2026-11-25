using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.App.Views;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
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
    private readonly CompanyBrandingService _branding;
    private readonly ILogger<WorkspaceWindowViewModel> _logger;

    private string? _pendingActionNavKey;

    public WorkspaceWindowViewModel(
        ISessionContext session,
        IPermissionService permissions,
        ILocalStoragePaths paths,
        IEnumerable<IAppModule> modules,
        IServiceProvider services,
        IEventBus eventBus,
        ThemeService theme,
        CompanyBrandingService branding,
        ILogger<WorkspaceWindowViewModel> logger)
    {
        _session = session;
        _permissions = permissions;
        _paths = paths;
        _modules = modules;
        _services = services;
        _eventBus = eventBus;
        _theme = theme;
        _branding = branding;
        _logger = logger;

        _permissions.PolicyChanged += (_, _) => PolicyRefreshRequested?.Invoke(this, EventArgs.Empty);
        _eventBus.Subscribe<InternalNoticeEvent>(evt =>
        {
            ActiveNoticeBanner = $"[{evt.SenderAdminName}] {evt.Title}: {evt.Message}";
            _pendingActionNavKey = "overview";
            HasNoticeAction = false;
            NoticeReceived?.Invoke(this, evt);
        });
        _eventBus.Subscribe<TaskAssignedNotificationEvent>(evt =>
        {
            ActiveNoticeBanner = $"📌 Việc mới được giao từ {evt.AssignerFullName} [{evt.Priority}]: {evt.Title}";
            _pendingActionNavKey = "core.tasks";
            HasNoticeAction = true;
        });
        _eventBus.Subscribe<ChatMessageReceivedEvent>(evt =>
        {
            if (SelectedNavItem?.Key != "core.chat")
            {
                ActiveNoticeBanner = $"💬 Tin nhắn mới từ {evt.SenderFullName}: {evt.Preview}";
                _pendingActionNavKey = "core.chat";
                HasNoticeAction = true;
            }
        });
        _eventBus.Subscribe<UserProfileUpdatedEvent>(evt =>
        {
            UserSubtitle = $"{evt.FullName} (@{evt.Username}) • {CompanyRoles.FormatRolesBilingual(evt.Role)}";
        });
        _eventBus.Subscribe<CompanyLogoUpdatedEvent>(_ => LogoRefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<NavigationItemModel> NavigationItems { get; } = [];

    [ObservableProperty] private NavigationItemModel? _selectedNavItem;
    [ObservableProperty] private object? _activeView;
    [ObservableProperty] private string _windowTitle = "Ez Company — Workspace";
    [ObservableProperty] private string _companyTitle = string.Empty;
    [ObservableProperty] private string _userSubtitle = string.Empty;
    [ObservableProperty] private BitmapImage? _companyLogoImage;
    [ObservableProperty] private bool _isPolicyLocked;
    [ObservableProperty] private string? _policyLockReason;
    [ObservableProperty] private string? _activeNoticeBanner;
    [ObservableProperty] private bool _hasNoticeAction;
    [ObservableProperty] private bool _isSettingsSelected;

    public event EventHandler? PolicyRefreshRequested;
    public event EventHandler? LogoutRequested;
    public event EventHandler? LogoRefreshRequested;
    public event EventHandler<InternalNoticeEvent>? NoticeReceived;

    public async Task InitializeWorkspaceAsync()
    {
        WindowTitle = $"Ez Company — {_session.CompanyName} ({_paths.ProfileName})";
        CompanyTitle = $"{_session.CompanyName} (MST: {_session.TaxCode})";
        UserSubtitle = $"{_session.FullName} (@{_session.Username}) • {CompanyRoles.FormatRolesBilingual(_session.Role)}";
        await RefreshCompanyLogoAsync();
        RebuildNavigation();
    }

    public async Task RefreshCompanyLogoAsync()
    {
        byte[]? bytes = await _branding.GetCompanyLogoBytesAsync();
        CompanyLogoImage = CompanyLogoImageHelper.LoadFromBytesOrDefault(bytes);
    }

    public void RebuildNavigation()
    {
        string? previousKey = IsSettingsSelected ? "core.settings" : SelectedNavItem?.Key;

        IsPolicyLocked = _permissions.IsLocked;
        PolicyLockReason = _permissions.LockReason;

        NavigationItems.Clear();

        NavigationItems.Add(new NavigationItemModel
        {
            Key = "overview",
            Title = "Tổng quan",
            Subtitle = "Chào mừng, Lịch trình & Vai trò",
            CreateContent = () =>
            {
                var view = (WelcomeDashboardView)_services.GetService(typeof(WelcomeDashboardView))!;
                if (view.DataContext is WelcomeDashboardViewModel vm)
                {
                    vm.NavigateRequested += (_, key) => OpenNavigationItem(key);
                }
                return view;
            }
        });

        if (!_permissions.IsLocked)
        {
            NavigationItems.Add(new NavigationItemModel
            {
                Key = "core.tasks",
                Title = "Giao việc & Bàn giao",
                Subtitle = "Đính kèm file, Thông báo & Lịch sử",
                CreateContent = () => _services.GetService(typeof(TaskManagementView))!
            });

            NavigationItems.Add(new NavigationItemModel
            {
                Key = "core.chat",
                Title = "Chat nội bộ",
                Subtitle = "Chat riêng, Nhóm & Ghim việc P2P",
                CreateContent = () => _services.GetService(typeof(ChatWorkspaceView))!
            });

            NavigationItems.Add(new NavigationItemModel
            {
                Key = "core.hr",
                Title = "Nhân sự & Chấm công",
                Subtitle = "Hồ sơ NV, Check-in/out & Excel",
                CreateContent = () => _services.GetService(typeof(HrAttendanceView))!
            });
        }

        if (_session.IsDirectorNode && !_permissions.IsLocked)
        {
            NavigationItems.Add(new NavigationItemModel
            {
                Key = "director.dashboard",
                Title = "Quản trị Giám đốc",
                Subtitle = "Manager (Giám đốc) • LAN & SAS",
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

        if (previousKey == "core.settings")
        {
            OpenSettings();
        }
        else
        {
            NavigationItemModel? next = NavigationItems.FirstOrDefault(x => x.Key == previousKey)
                                        ?? NavigationItems.FirstOrDefault();
            SelectedNavItem = next;
        }
    }

    public void OpenNavigationItem(string key)
    {
        if (string.Equals(key, "core.settings", StringComparison.OrdinalIgnoreCase))
        {
            OpenSettings();
            return;
        }

        NavigationItemModel? item = NavigationItems.FirstOrDefault(x => x.Key == key);
        if (item is not null)
        {
            IsSettingsSelected = false;
            SelectedNavItem = item;
        }
    }

    partial void OnSelectedNavItemChanged(NavigationItemModel? value)
    {
        if (value is null)
        {
            return;
        }

        IsSettingsSelected = false;
        ActiveView = value.CreateContent();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SelectedNavItem = null;
        IsSettingsSelected = true;
        ActiveView = _services.GetService(typeof(AccountAndAppearanceView))!;
    }

    [RelayCommand]
    private void OpenNoticeTarget()
    {
        if (!string.IsNullOrWhiteSpace(_pendingActionNavKey))
        {
            OpenNavigationItem(_pendingActionNavKey);
        }

        ActiveNoticeBanner = null;
        HasNoticeAction = false;
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.ToggleTheme();

    [RelayCommand]
    private void DismissNotice()
    {
        ActiveNoticeBanner = null;
        HasNoticeAction = false;
    }

    public async Task OpenDbInspectorAsync(string dbFilePath)
    {
        NavigationItemModel? item = NavigationItems.FirstOrDefault(x => x.Key == "director.db_inspector");
        if (item is not null)
        {
            IsSettingsSelected = false;
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
