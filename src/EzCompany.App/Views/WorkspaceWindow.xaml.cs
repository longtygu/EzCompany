using System.Windows;
using EzCompany.App.Controls;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;
using EzCompany.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.App.Views;

public partial class WorkspaceWindow : EzFluentWindow
{
    private readonly IServiceProvider _services;
    private readonly IWorkspaceWindowAccessor _windowAccessor;
    private readonly ChatNotificationPopupService _chatPopupService;
    private readonly ChatDockCoordinator _chatDockCoordinator;
    private EventHandler<Guid>? _popupOpenHandler;

    public WorkspaceWindow(
        WorkspaceWindowViewModel viewModel,
        IServiceProvider services,
        IWorkspaceWindowAccessor windowAccessor,
        AppearanceSettingsService appearance,
        ChatNotificationPopupService chatPopupService,
        ChatDockCoordinator chatDockCoordinator)
    {
        _services = services;
        _windowAccessor = windowAccessor;
        _chatPopupService = chatPopupService;
        _chatDockCoordinator = chatDockCoordinator;
        DataContext = viewModel;
        InitializeComponent();
        appearance.ApplyToWindow(this);

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
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                Activate();
                await viewModel.OpenChatConversationAsync(convId);
            });
        };
        _chatPopupService.OpenChatConversationRequested += _popupOpenHandler;

        Loaded += async (_, _) =>
        {
            appearance.ApplyToWindow(this);
            _windowAccessor.RegisterWindow(this);
            await viewModel.InitializeWorkspaceAsync();
        };
        Closed += (_, _) =>
        {
            if (_popupOpenHandler is not null)
            {
                _chatPopupService.OpenChatConversationRequested -= _popupOpenHandler;
            }
            _chatDockCoordinator.CloseDetachedWindowOnShutdown();
            _chatPopupService.CloseAllPopups();
            _windowAccessor.UnregisterWindow(this);
        };
    }
}
