using System.Windows.Controls;
using System.Windows.Threading;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class ExecutiveDashboardView : UserControl
{
    private readonly DispatcherTimer _countdownTimer;

    public ExecutiveDashboardView(ExecutiveDashboardViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            viewModel.RefreshPairingCountdown();
            viewModel.RefreshPendingPairingsList();
        };

        Loaded += async (_, _) =>
        {
            _countdownTimer.Start();
            await viewModel.RefreshAllAsync();
        };
        Unloaded += (_, _) => _countdownTimer.Stop();
    }
}
