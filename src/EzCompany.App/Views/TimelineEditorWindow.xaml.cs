using EzCompany.App.Controls;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;
using EzCompany.Core.Data.Entities;

namespace EzCompany.App.Views;

public partial class TimelineEditorWindow : EzFluentWindow
{
    private readonly TimelineEditorViewModel _viewModel;

    public TimelineEditorWindow(TimelineEditorViewModel viewModel, AppearanceSettingsService appearance)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        appearance.ApplyToWindow(this);
        Loaded += (_, _) => appearance.ApplyToWindow(this);
    }

    public Task InitializeAsync(TimelinePlanEntity? initialPlan = null) =>
        _viewModel.LoadAsync(initialPlan);
}
