using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EzCompany.App.Views;

public sealed partial class NavigationItemModel : ObservableObject
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required Func<object> CreateContent { get; init; }
    public Action<object>? OnActivated { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private int _badgeCount;

    public bool HasBadge => BadgeCount > 0;
}

public sealed partial class NavigationGroupModel : ObservableObject
{
    public required string GroupKey { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<NavigationItemModel> Items { get; } = [];
}
