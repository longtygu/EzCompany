using System.Windows.Controls;

namespace EzCompany.App.Views;

public sealed class NavigationItemModel
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required Func<object> CreateContent { get; init; }
}
