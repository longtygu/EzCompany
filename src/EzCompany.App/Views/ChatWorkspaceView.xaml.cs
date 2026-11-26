using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class ChatWorkspaceView : UserControl
{
    private readonly ChatWorkspaceViewModel _viewModel;
    private readonly ChatNotificationPopupService _popupService;
    private readonly DispatcherTimer _pollTimer;

    public ChatWorkspaceView(ChatWorkspaceViewModel viewModel, ChatNotificationPopupService popupService)
    {
        _viewModel = viewModel;
        _popupService = popupService;
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

        Loaded += async (_, _) =>
        {
            _popupService.IsChatViewActive = true;
            _popupService.ActiveViewingConversationId = _viewModel.SelectedConversation?.Id;
            await _viewModel.RefreshAllAsync();
            _pollTimer.Start();
        };

        Unloaded += (_, _) =>
        {
            _pollTimer.Stop();
            _popupService.IsChatViewActive = false;
            _popupService.ActiveViewingConversationId = null;
            _viewModel.Dispose();
        };
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
