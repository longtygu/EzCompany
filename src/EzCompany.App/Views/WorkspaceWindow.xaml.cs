using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using EzCompany.App.Controls;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Collaboration;
using EzCompany.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.App.Views;

public partial class WorkspaceWindow : EzFluentWindow
{
    private readonly IServiceProvider _services;
    private readonly IWorkspaceWindowAccessor _windowAccessor;
    private readonly AppearanceSettingsService _appearance;
    private readonly ChatNotificationPopupService _chatPopupService;
    private readonly ChatDockCoordinator _chatDockCoordinator;
    private readonly SystemTrayService _trayService;
    private readonly TaskDeadlineReminderService _reminderService;
    private readonly HrAttendanceService _hrService;
    private readonly LanCollaborationClient _collabClient;
    private readonly DispatcherTimer _reminderTimer;
    private EventHandler<Guid>? _popupOpenHandler;
    private bool _isExplicitExit;
    private bool _isLoggingOut;
    private bool _shownTrayBalloon;

    public WorkspaceWindow(
        WorkspaceWindowViewModel viewModel,
        IServiceProvider services,
        IWorkspaceWindowAccessor windowAccessor,
        AppearanceSettingsService appearance,
        ChatNotificationPopupService chatPopupService,
        ChatDockCoordinator chatDockCoordinator,
        SystemTrayService trayService,
        TaskDeadlineReminderService reminderService,
        HrAttendanceService hrService,
        LanCollaborationClient collabClient)
    {
        _services = services;
        _windowAccessor = windowAccessor;
        _appearance = appearance;
        _chatPopupService = chatPopupService;
        _chatDockCoordinator = chatDockCoordinator;
        _trayService = trayService;
        _reminderService = reminderService;
        _hrService = hrService;
        _collabClient = collabClient;
        DataContext = viewModel;
        InitializeComponent();
        appearance.ApplyToWindow(this);

        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _reminderTimer.Tick += async (_, _) => await _reminderService.CheckAndNotifyAsync();

        void RestoreWindow()
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsVisible) Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            });
        }

        void OnTrayOpen(object? s, EventArgs e) => RestoreWindow();
        void OnTrayChat(object? s, EventArgs e)
        {
            RestoreWindow();
            Dispatcher.Invoke(() => viewModel.OpenNavigationItem("core.chat"));
        }

        void OnTrayCheckIn(object? s, EventArgs e)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    AttendanceRecordEntity rec = await _hrService.CheckInTodayAsync();
                    _ = await _collabClient.PushAttendanceToDirectorsAsync(rec);
                    _trayService.ShowBalloon("Ez Company — Chấm công", $"Đã check-in lúc {rec.CheckInUtc?.ToLocalTime():HH:mm}");
                }
                catch
                {
                }
            });
        }

        void OnTrayExit(object? s, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _isExplicitExit = true;
                Close();
            });
        }

        _trayService.OpenMainWindowRequested += OnTrayOpen;
        _trayService.OpenChatRequested += OnTrayChat;
        _trayService.QuickCheckInRequested += OnTrayCheckIn;
        _trayService.ExitRequested += OnTrayExit;

        viewModel.PolicyRefreshRequested += (_, _) =>
        {
            Dispatcher.Invoke(() => viewModel.RebuildNavigation());
        };

        viewModel.LogoRefreshRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(async () => await viewModel.RefreshCompanyLogoAsync());
        };

        viewModel.LogoutRequested += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                _isLoggingOut = true;
                _chatPopupService.CloseAllPopups();
                MainWindow main = _services.GetRequiredService<MainWindow>();
                if (Application.Current is not null)
                {
                    Application.Current.MainWindow = main;
                }

                main.Show();
                Close();
            });
        };

        _popupOpenHandler = (_, convId) =>
        {
            Dispatcher.InvokeAsync(async () =>
            {
                RestoreWindow();
                await viewModel.OpenChatConversationAsync(convId);
            });
        };
        _chatPopupService.OpenChatConversationRequested += _popupOpenHandler;

        Loaded += async (_, _) =>
        {
            appearance.ApplyToWindow(this);
            _windowAccessor.RegisterWindow(this);
            _trayService.Attach(this);
            _reminderTimer.Start();
            await _reminderService.CheckAndNotifyAsync();
            await viewModel.InitializeWorkspaceAsync();
        };

        Closing += OnWorkspaceClosing;

        Closed += (_, _) =>
        {
            _reminderTimer.Stop();
            _trayService.OpenMainWindowRequested -= OnTrayOpen;
            _trayService.OpenChatRequested -= OnTrayChat;
            _trayService.QuickCheckInRequested -= OnTrayCheckIn;
            _trayService.ExitRequested -= OnTrayExit;
            _trayService.Dispose();

            if (_popupOpenHandler is not null)
            {
                _chatPopupService.OpenChatConversationRequested -= _popupOpenHandler;
            }
            _chatDockCoordinator.CloseDetachedWindowOnShutdown();
            _chatPopupService.CloseAllPopups();
            _windowAccessor.UnregisterWindow(this);
        };
    }

    private void OnWorkspaceClosing(object? sender, CancelEventArgs e)
    {
        if (_isExplicitExit || _isLoggingOut || !_appearance.Current.MinimizeToTrayOnClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        if (!_shownTrayBalloon)
        {
            _shownTrayBalloon = true;
            _trayService.ShowBalloon("Ez Company", "Ứng dụng đang chạy nền ở khay hệ thống để nhận tin nhắn & việc mới.");
        }
    }
}
