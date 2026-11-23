using System.Windows.Controls;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class DbInspectorView : UserControl
{
    public DbInspectorView(DbInspectorViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
