using System.Windows;
using EzCompany.App.Controls;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.App.Views;

public partial class MainWindow : EzFluentWindow
{
    private readonly IServiceProvider _services;

    public MainWindow(
        MainWindowViewModel viewModel,
        IServiceProvider services,
        AppearanceSettingsService appearance)
    {
        _services = services;
        DataContext = viewModel;
        InitializeComponent();
        appearance.ApplyToWindow(this);

        viewModel.AuthenticationCompleted += OnAuthenticationCompleted;
        viewModel.LanPeersRefreshRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(() => viewModel.RefreshDiscoveredDirectors());
        };
        Loaded += async (_, _) =>
        {
            appearance.ApplyToWindow(this);
            await viewModel.LoadStateAsync();
        };
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    private async void GenesisSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExecuteGenesisAsync(
            GenesisLoginPasswordBox.Password,
            GenesisRecoveryPasswordBox.Password);
    }

    private async void RestoreSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExecuteRestoreDirectorAsync(
            RestoreRecoveryPasswordBox.Password,
            RestoreLoginPasswordBox.Password);
    }

    private async void OfflineLoginButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExecuteOfflineLoginAsync(OfflineLoginPasswordBox.Password);
    }

    private async void JoinLanButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExecuteJoinLanAsync(LanLoginPasswordBox.Password);
    }

    private async void EmployeeResetSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExecuteEmployeePasswordResetAsync(
            EmployeeResetNewPasswordBox.Password,
            EmployeeResetConfirmPasswordBox.Password);
    }

    private async void DirectorRecoverAccountSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExecuteDirectorAccountRecoveryAsync(
            DirectorRecoverKitPasswordBox.Password,
            DirectorRecoverNewPasswordBox.Password,
            DirectorRecoverConfirmPasswordBox.Password);
    }

    private void OnAuthenticationCompleted(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            WorkspaceWindow workspace = _services.GetRequiredService<WorkspaceWindow>();
            if (Application.Current is not null)
            {
                Application.Current.MainWindow = workspace;
            }

            workspace.Show();
            Close();
        });
    }
}
