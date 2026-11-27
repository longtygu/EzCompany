using System.Windows;
using System.Windows.Controls;
using EzCompany.App.ViewModels;
using EzCompany.Core.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.App.Views;

public partial class WelcomeDashboardView : UserControl
{
    private readonly WelcomeDashboardViewModel _viewModel;
    private readonly IServiceProvider _services;

    public WelcomeDashboardView(WelcomeDashboardViewModel viewModel, IServiceProvider services)
    {
        _viewModel = viewModel;
        _services = services;
        DataContext = viewModel;
        InitializeComponent();

        viewModel.RefreshRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(async () => await viewModel.RefreshAsync());
        };
        viewModel.OpenTimelineEditorRequested += async (_, plan) => await OpenTimelineEditorWindowAsync(plan);
        ParallelTimeline.SizeChanged += (_, e) => _viewModel.UpdateTimelineViewportWidth(e.NewSize.Width);
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }

    private async void ParallelTimeline_PlanClicked(object? sender, TimelinePlanEntity plan) =>
        await OpenTimelineEditorWindowAsync(plan);

    private async Task OpenTimelineEditorWindowAsync(TimelinePlanEntity? plan)
    {
        TimelineEditorWindow win = _services.GetRequiredService<TimelineEditorWindow>();
        if (Application.Current?.MainWindow is not null)
        {
            win.Owner = Application.Current.MainWindow;
        }

        if (win.DataContext is TimelineEditorViewModel editorVm)
        {
            editorVm.PlansChanged += async (_, _) => await _viewModel.RefreshAsync();
        }

        win.Closed += async (_, _) => await _viewModel.RefreshAsync();
        await win.InitializeAsync(plan);
        win.Show();
    }
}
