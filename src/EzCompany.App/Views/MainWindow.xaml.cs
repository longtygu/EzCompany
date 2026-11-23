using System.Windows;
using EzCompany.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace EzCompany.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly IServiceProvider _services;

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider services)
    {
        _services = services;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.AuthenticationCompleted += OnAuthenticationCompleted;
        viewModel.LanPeersRefreshRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(() => viewModel.RefreshDiscoveredDirectors());
        };
        Loaded += async (_, _) => await viewModel.LoadStateAsync();
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
