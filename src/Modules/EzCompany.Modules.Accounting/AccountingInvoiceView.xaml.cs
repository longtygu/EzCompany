using System.Windows.Controls;

namespace EzCompany.Modules.Accounting;

public partial class AccountingInvoiceView : UserControl
{
    public AccountingInvoiceView(AccountingInvoiceViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.RefreshRequested += (_, _) => Dispatcher.InvokeAsync(async () => await viewModel.LoadSavedAsync());
        Loaded += async (_, _) => await viewModel.LoadSavedAsync();
    }
}
