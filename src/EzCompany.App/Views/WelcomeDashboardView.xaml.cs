using System.Windows.Controls;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class WelcomeDashboardView : UserControl
{
    public WelcomeDashboardView(WelcomeDashboardViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        viewModel.RefreshRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(async () => await viewModel.RefreshAsync());
        };
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }
}
