using System.Windows.Controls;

namespace EzCompany.Modules.Legal;

public partial class LegalContractDiffView : UserControl
{
    public LegalContractDiffView(LegalContractDiffViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.LoadHistoryAsync();
    }

    private void DiffGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is not null)
        {
            grid.ScrollIntoView(grid.SelectedItem);
        }
    }
}
