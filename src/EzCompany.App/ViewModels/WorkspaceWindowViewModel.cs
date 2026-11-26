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
    private readonly ChatNotificationPopupService? _chatPopupService;

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
        ILogger<WorkspaceWindowViewModel> logger,
        ChatNotificationPopupService? chatPopupService = null)
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
        _chatPopupService = chatPopupService;

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
            if (!string.IsNullOrWhiteSpace(evt.SenderMachineId) &&
                string.Equals(evt.SenderMachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (SelectedNavItem?.Key != "core.chat")
            {
                NavigationItemModel? chatNav = NavigationItems.FirstOrDefault(x => x.Key == "core.chat");
                if (chatNav is not null)
                {
                    chatNav.BadgeCount++;
                }

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

    public ObservableCollection<NavigationGroupModel> NavigationGroups { get; } = [];
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

        NavigationGroups.Clear();
        NavigationItems.Clear();

        // 1. Common Workspace Group
        var commonGroup = new NavigationGroupModel
        {
            GroupKey = "group.common",
            Title = "Không gian chung",
            Subtitle = "Tổng quan, Chat P2P & Giao việc",
            IsExpanded = true
        };

        AddNavItem(commonGroup, new NavigationItemModel
        {
            Key = "overview",
            Title = "Tổng quan",
            Subtitle = "Chào mừng, Lịch trình & Vai trò",
            CreateContent = () =>
            {
                var view = _services.GetService(typeof(WelcomeDashboardView)) as WelcomeDashboardView;
                if (view?.DataContext is WelcomeDashboardViewModel vm)
                {
                    vm.NavigateRequested += (_, key) => OpenNavigationItem(key);
                }
                return view!;
            }
        });

        if (!_permissions.IsLocked)
        {
            AddNavItem(commonGroup, new NavigationItemModel
            {
                Key = "core.chat",
                Title = "Chat nội bộ",
                Subtitle = "1s Realtime, Popup & Sticker",
                CreateContent = () => _services.GetService(typeof(ChatWorkspaceView))!
            });

            AddNavItem(commonGroup, new NavigationItemModel
            {
                Key = "core.tasks",
                Title = "Giao việc & Bàn giao",
                Subtitle = "Đính kèm file, Thông báo & Lịch sử",
                CreateContent = () => _services.GetService(typeof(TaskManagementView))!
            });
        }

        NavigationGroups.Add(commonGroup);

        // 2. Director (Manager) Role Group
        if (_session.IsDirectorNode && !_permissions.IsLocked)
        {
            var directorGroup = new NavigationGroupModel
            {
                GroupKey = "group.director",
                Title = "Manager (Giám đốc)",
                Subtitle = "Điều hành LAN, SAS & Dữ liệu",
                IsExpanded = true
            };

            AddNavItem(directorGroup, new NavigationItemModel
            {
                Key = "director.dashboard",
                Title = "Bảng điều hành Giám đốc",
                Subtitle = "Lưới máy trạm, Telemetry & SAS",
                CreateContent = () =>
                {
                    var view = _services.GetService(typeof(ExecutiveDashboardView)) as ExecutiveDashboardView;
                    if (view?.DataContext is ExecutiveDashboardViewModel vm)
                    {
                        vm.InspectDatabaseRequested += async (_, dbPath) => await OpenDbInspectorAsync(dbPath);
                    }
                    return view!;
                }
            });

            AddNavItem(directorGroup, new NavigationItemModel
            {
                Key = "director.db_inspector",
                Title = "Read-Only DB Inspector",
                Subtitle = "Xem bản chụp .db chỉ đọc",
                CreateContent = () => _services.GetService(typeof(DbInspectorView))!
            });

            NavigationGroups.Add(directorGroup);
        }

        // 3. HR (Nhân sự) Role Group: Profiles, Attendance, Payroll
        if (!_permissions.IsLocked)
        {
            bool expandHrByDefault = CompanyRoles.HasRole(_session.Role, CompanyRoles.Hr) || _session.IsDirectorNode;
            var hrGroup = new NavigationGroupModel
            {
                GroupKey = "group.hr",
                Title = "HR (Nhân sự)",
                Subtitle = "Hồ sơ, Chấm công & Tính lương",
                IsExpanded = expandHrByDefault
            };

            AddNavItem(hrGroup, CreateHrNavItem("core.hr.profiles", "Hồ sơ nhân viên", "Khay mở rộng • Thêm/Sửa/Xóa", 0));
            AddNavItem(hrGroup, CreateHrNavItem("core.hr.attendance", "Chấm công", "Giờ tùy chỉnh & Bảng full ngày", 1));
            AddNavItem(hrGroup, CreateHrNavItem("core.hr.payroll", "Tính lương tự động", "Công, Tăng ca & Xuất Excel", 2));
            NavigationGroups.Add(hrGroup);

            // 4. Role-based Module Groups (Accounting, Legal, Office, Extensions)
            IAppModule[] allowedModules = _modules
                .OrderBy(m => m.Metadata.DisplayOrder)
                .Where(m => _permissions.HasModuleAccess(m.Metadata.ModuleId))
                .ToArray();

            NavigationGroupModel? accGroup = null;
            NavigationGroupModel? legalGroup = null;
            NavigationGroupModel? officeGroup = null;
            NavigationGroupModel? extGroup = null;

            foreach (IAppModule module in allowedModules)
            {
                IAppModule captured = module;
                var navItem = new NavigationItemModel
                {
                    Key = captured.Metadata.ModuleId,
                    Title = captured.Metadata.DisplayName,
                    Subtitle = captured.Metadata.Category,
                    CreateContent = () => captured.CreateView(_services)
                };

                if (string.Equals(captured.Metadata.ModuleId, ModuleIds.AccountingInvoiceXml, StringComparison.OrdinalIgnoreCase))
                {
                    accGroup ??= new NavigationGroupModel
                    {
                        GroupKey = "group.accounting",
                        Title = "Accounting (Kế toán)",
                        Subtitle = "Hóa đơn XML & Công cụ kế toán",
                        IsExpanded = CompanyRoles.HasRole(_session.Role, CompanyRoles.Accounting) || _session.IsDirectorNode
                    };
                    AddNavItem(accGroup, navItem);
                }
                else if (string.Equals(captured.Metadata.ModuleId, ModuleIds.LegalContractDiff, StringComparison.OrdinalIgnoreCase))
                {
                    legalGroup ??= new NavigationGroupModel
                    {
                        GroupKey = "group.legal",
                        Title = "Legal (Pháp chế)",
                        Subtitle = "Rà soát & So sánh Hợp đồng",
                        IsExpanded = CompanyRoles.HasRole(_session.Role, CompanyRoles.Legal) || _session.IsDirectorNode
                    };
                    AddNavItem(legalGroup, navItem);
                }
                else if (string.Equals(captured.Metadata.ModuleId, ModuleIds.OfficePdfStudio, StringComparison.OrdinalIgnoreCase))
                {
                    officeGroup ??= new NavigationGroupModel
                    {
                        GroupKey = "group.office",
                        Title = "Staff & Office (Văn phòng)",
                        Subtitle = "Xử lý PDF & Tiện ích dùng chung",
                        IsExpanded = CompanyRoles.HasRole(_session.Role, CompanyRoles.Staff) || _session.IsDirectorNode
                    };
                    AddNavItem(officeGroup, navItem);
                }
                else
                {
                    extGroup ??= new NavigationGroupModel
                    {
                        GroupKey = "group.extensions",
                        Title = "Tiện ích mở rộng",
                        Subtitle = "Module cộng đồng",
                        IsExpanded = false
                    };
                    AddNavItem(extGroup, navItem);
                }
            }

            if (accGroup is not null) NavigationGroups.Add(accGroup);
            if (legalGroup is not null) NavigationGroups.Add(legalGroup);
            if (officeGroup is not null) NavigationGroups.Add(officeGroup);
            if (extGroup is not null) NavigationGroups.Add(extGroup);
        }

        if (previousKey == "core.settings")
        {
            OpenSettings();
        }
        else
        {
            NavigationItemModel? next = FindNavItemByKey(previousKey) ?? NavigationItems.FirstOrDefault();
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

        NavigationItemModel? item = FindNavItemByKey(key);
        if (item is not null)
        {
            NavigationGroupModel? parent = NavigationGroups.FirstOrDefault(g => g.Items.Contains(item));
            if (parent is not null)
            {
                parent.IsExpanded = true;
            }

            IsSettingsSelected = false;
            SelectedNavItem = item;
        }
    }

    public async Task OpenChatConversationAsync(Guid conversationId)
    {
        OpenNavigationItem("core.chat");
        if (ActiveView is ChatWorkspaceView chatView && chatView.DataContext is ChatWorkspaceViewModel chatVm)
        {
            await chatVm.SelectConversationByIdAsync(conversationId);
        }
    }

    [RelayCommand]
    private void SelectNavItem(NavigationItemModel? item)
    {
        if (item is null)
        {
            return;
        }

        NavigationGroupModel? parent = NavigationGroups.FirstOrDefault(g => g.Items.Contains(item));
        if (parent is not null)
        {
            parent.IsExpanded = true;
        }

        SelectedNavItem = item;
    }

    partial void OnSelectedNavItemChanged(NavigationItemModel? value)
    {
        foreach (NavigationItemModel nav in NavigationItems)
        {
            nav.IsSelected = nav == value;
        }

        if (value is null)
        {
            return;
        }

        IsSettingsSelected = false;

        if (string.Equals(value.Key, "core.chat", StringComparison.OrdinalIgnoreCase))
        {
            value.BadgeCount = 0;
        }

        if (value.Key.StartsWith("core.hr.", StringComparison.OrdinalIgnoreCase) && ActiveView is HrAttendanceView existingHrView)
        {
            value.OnActivated?.Invoke(existingHrView);
            return;
        }

        object created = value.CreateContent();
        value.OnActivated?.Invoke(created);
        ActiveView = created;
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
        NavigationItemModel? item = FindNavItemByKey("director.db_inspector");
        if (item is not null)
        {
            OpenNavigationItem(item.Key);
            if (ActiveView is DbInspectorView inspectorView && inspectorView.DataContext is DbInspectorViewModel vm)
            {
                await vm.LoadFileAsync(dbFilePath);
            }
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _chatPopupService?.CloseAllPopups();
        _session.ClearAuthentication();
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    private NavigationItemModel? FindNavItemByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string mappedKey = string.Equals(key, "core.hr", StringComparison.OrdinalIgnoreCase)
            ? "core.hr.profiles"
            : key;

        return NavigationItems.FirstOrDefault(x => string.Equals(x.Key, mappedKey, StringComparison.OrdinalIgnoreCase));
    }

    private void AddNavItem(NavigationGroupModel group, NavigationItemModel item)
    {
        group.Items.Add(item);
        NavigationItems.Add(item);
    }

    private NavigationItemModel CreateHrNavItem(string key, string title, string subtitle, int targetTabIndex) => new()
    {
        Key = key,
        Title = title,
        Subtitle = subtitle,
        CreateContent = () => _services.GetService(typeof(HrAttendanceView))!,
        OnActivated = view =>
        {
            if (view is HrAttendanceView hrView && hrView.DataContext is HrAttendanceViewModel vm)
            {
                vm.SelectedTabIndex = targetTabIndex;
            }
        }
    };
}
