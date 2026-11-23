using System.Windows;
using EzCompany.App.ViewModels;
using EzCompany.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace EzCompany.App.Views;

public partial class WorkspaceWindow : FluentWindow
{
    private readonly IServiceProvider _services;
    private readonly IWorkspaceWindowAccessor _windowAccessor;

    public WorkspaceWindow(
        WorkspaceWindowViewModel viewModel,
        IServiceProvider services,
        IWorkspaceWindowAccessor windowAccessor)
    {
        _services = services;
        _windowAccessor = windowAccessor;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.PolicyRefreshRequested += (_, _) =>
        {
            Dispatcher.Invoke(() => viewModel.RebuildNavigation());
        };

        viewModel.LogoutRequested += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                MainWindow main = _services.GetRequiredService<MainWindow>();
                if (Application.Current is not null)
                {
                    Application.Current.MainWindow = main;
                }

                main.Show();
                Close();
            });
        };

        Loaded += (_, _) =>
        {
            _windowAccessor.RegisterWindow(this);
            viewModel.InitializeWorkspace();
        };
        Closed += (_, _) => _windowAccessor.UnregisterWindow(this);
    }
}
