using System.Windows;
using System.Windows.Controls;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class AccountAndAppearanceView : UserControl
{
    public AccountAndAppearanceView(AccountAndAppearanceViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.LoadAllAsync();
    }

    private AccountAndAppearanceViewModel ViewModel => (AccountAndAppearanceViewModel)DataContext;

    private async void SaveCredentialsButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ChangeCredentialsAsync(
            CurrentPasswordBox.Password,
            NewPasswordBox.Password,
            ConfirmPasswordBox.Password);

        CurrentPasswordBox.Password = string.Empty;
        NewPasswordBox.Password = string.Empty;
        ConfirmPasswordBox.Password = string.Empty;
    }
}
