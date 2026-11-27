using System.Data;
using System.Windows;
using System.Windows.Controls;
using EzCompany.App.Controls;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class AttendanceDetailWindow : EzFluentWindow
{
    private readonly AttendanceDetailViewModel _viewModel;

    public AttendanceDetailWindow(AttendanceDetailViewModel viewModel, AppearanceSettingsService appearance)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        appearance.ApplyToWindow(this);
        viewModel.ConfirmCallback = (message, title) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        Loaded += (_, _) => appearance.ApplyToWindow(this);
    }

    private async void MatrixGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (e.AddedCells.Count == 0)
        {
            return;
        }

        DataGridCellInfo cell = e.AddedCells[0];
        if (cell.Item is not DataRowView row || cell.Column?.Header is null)
        {
            return;
        }

        if (!int.TryParse(cell.Column.Header.ToString(), out int day))
        {
            return;
        }

        string? employeeCode = row["Mã NV"]?.ToString();
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return;
        }

        await _viewModel.SelectMatrixCellAsync(employeeCode, day);
    }
}
