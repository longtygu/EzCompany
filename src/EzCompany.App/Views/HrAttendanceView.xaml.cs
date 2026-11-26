using System.Windows;
using System.Windows.Controls;
using EzCompany.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.App.Views;

public partial class HrAttendanceView : UserControl
{
    private readonly IServiceProvider _services;

    public HrAttendanceView(HrAttendanceViewModel viewModel, IServiceProvider services)
    {
        _services = services;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.ConfirmDeleteCallback = (message, title) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

        viewModel.OpenFullAttendanceWindowRequested += async (_, ym) =>
        {
            AttendanceDetailWindow win = _services.GetRequiredService<AttendanceDetailWindow>();
            if (Application.Current?.MainWindow is not null)
            {
                win.Owner = Application.Current.MainWindow;
            }

            win.Closed += async (_, _) => await viewModel.RefreshAllAsync();
            if (win.DataContext is AttendanceDetailViewModel detailVm)
            {
                detailVm.DataModified += async (_, _) => await viewModel.RefreshAllAsync();
                detailVm.SelectedYear = ym.Year;
                detailVm.SelectedMonth = ym.Month;
            }

            win.Show();
            if (win.DataContext is AttendanceDetailViewModel vm)
            {
                await vm.RefreshAsync();
            }
        };

        Loaded += async (_, _) => await viewModel.RefreshAllAsync();
    }
}
