using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class ExecutiveDashboardView : UserControl
{
    private readonly ExecutiveDashboardViewModel _viewModel;
    private readonly DispatcherTimer _countdownTimer;

    public ExecutiveDashboardView(ExecutiveDashboardViewModel viewModel, AppearanceSettingsService appearance)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            viewModel.RefreshPairingCountdown();
            viewModel.RefreshPendingPairingsList();
        };

        viewModel.PasswordResetsRefreshRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(async () => await viewModel.RefreshPendingPasswordResetsAsync());
        };
        viewModel.ApprovalHubRefreshRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(async () => await viewModel.RefreshApprovalHubAsync());
        };
        viewModel.SnapshotViewerRequested += (_, viewerVm) =>
        {
            var win = new SnapshotViewerWindow(viewerVm, appearance)
            {
                Owner = Window.GetWindow(this)
            };
            win.Show();
        };
        Loaded += async (_, _) =>
        {
            _countdownTimer.Start();
            await viewModel.RefreshAllAsync();
        };
        Unloaded += (_, _) => _countdownTimer.Stop();
    }

    private void SnapshotList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedSnapshot is not null)
        {
            _viewModel.OpenSnapshotViewerCommand.Execute(null);
        }
    }

    private void SnapshotPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && _viewModel.SelectedSnapshot is not null)
        {
            _viewModel.OpenSnapshotViewerCommand.Execute(null);
        }
    }
}
