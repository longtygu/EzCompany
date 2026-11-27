using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.App.Views;

public partial class ChatWorkspaceView : UserControl
{
    private readonly ChatWorkspaceViewModel _viewModel;
    private readonly ChatNotificationPopupService _popupService;
    private readonly ChatDockCoordinator _dockCoordinator;
    private readonly IServiceProvider _services;
    private readonly DispatcherTimer _pollTimer;
    private bool _hasInitializedOnce;

    public ChatWorkspaceView(
        ChatWorkspaceViewModel viewModel,
        ChatNotificationPopupService popupService,
        ChatDockCoordinator dockCoordinator,
        IServiceProvider services)
    {
        _viewModel = viewModel;
        _popupService = popupService;
        _dockCoordinator = dockCoordinator;
        _services = services;
        DataContext = viewModel;
        InitializeComponent();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _pollTimer.Tick += async (_, _) => await _viewModel.PollIncrementalAsync();

        _viewModel.RefreshRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(async () => await _viewModel.PollIncrementalAsync());
        };
        _viewModel.ScrollToBottomRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(() => MessageScrollViewer.ScrollToEnd());
        };
        _viewModel.OpenCreateGroupWindowRequested += async (_, _) =>
        {
            await OpenCreateGroupWindowAsync();
        };
        _viewModel.ToggleDetachRequested += (_, _) =>
        {
            if (_dockCoordinator.IsChatDetached)
            {
                _dockCoordinator.RedockToMain();
            }
            else if (Application.Current?.MainWindow is Window hostWindow)
            {
                _dockCoordinator.DetachChat(this, _viewModel, hostWindow);
            }
        };

        Loaded += async (_, _) =>
        {
            SuppressDisposeOnUnload = false;
            _popupService.IsChatViewActive = true;
            _popupService.ActiveViewingConversationId = _viewModel.SelectedConversation?.Id;

            if (!_hasInitializedOnce)
            {
                _hasInitializedOnce = true;
                await _viewModel.RefreshAllAsync();
            }
            else
            {
                await _viewModel.PollIncrementalAsync();
            }

            if (!_pollTimer.IsEnabled)
            {
                _pollTimer.Start();
            }
        };

        Unloaded += (_, _) =>
        {
            _pollTimer.Stop();
            if (SuppressDisposeOnUnload)
            {
                return;
            }

            _popupService.IsChatViewActive = false;
            _popupService.ActiveViewingConversationId = null;
            _viewModel.Dispose();
        };
    }

    public bool SuppressDisposeOnUnload { get; set; }

    private async Task OpenCreateGroupWindowAsync()
    {
        CreateChatGroupWindow dialog = _services.GetRequiredService<CreateChatGroupWindow>();
        Window? owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.SetAvailableMembers(_viewModel.PeerPresenceItems);
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.CreateGroupWithMembersAsync(dialog.CreatedGroupName, dialog.SelectedMembers);
        }
    }

    private async void MessageInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            await _viewModel.SendMessageAsync();
        }
    }
}
