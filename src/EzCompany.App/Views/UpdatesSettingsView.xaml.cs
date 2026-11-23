using System.Windows.Controls;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class UpdatesSettingsView : UserControl
{
    public UpdatesSettingsView(UpdatesSettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.Load();
    }
}
