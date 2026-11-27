using System.Windows.Controls;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class EmployeeRequestsView : UserControl
{
    public EmployeeRequestsView(EmployeeRequestsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.RefreshRequested += (_, _) => Dispatcher.InvokeAsync(async () => await viewModel.RefreshAllAsync());
        Loaded += async (_, _) => await viewModel.RefreshAllAsync();
    }
}
