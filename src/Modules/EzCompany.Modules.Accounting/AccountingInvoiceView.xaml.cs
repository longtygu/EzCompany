using System.Windows.Controls;

namespace EzCompany.Modules.Accounting;

public partial class AccountingInvoiceView : UserControl
{
    public AccountingInvoiceView(AccountingInvoiceViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.LoadSavedAsync();
    }
}
