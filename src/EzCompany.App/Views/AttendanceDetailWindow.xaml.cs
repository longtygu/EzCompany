using EzCompany.App.Controls;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class AttendanceDetailWindow : EzFluentWindow
{
    public AttendanceDetailWindow(AttendanceDetailViewModel viewModel, AppearanceSettingsService appearance)
    {
        DataContext = viewModel;
        InitializeComponent();
        appearance.ApplyToWindow(this);
        Loaded += (_, _) => appearance.ApplyToWindow(this);
    }
}
