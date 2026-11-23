using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EzCompany.Modules.Office;

public partial class OfficePdfStudioView : UserControl
{
    private string? _draggedItem;

    public OfficePdfStudioView(OfficePdfStudioViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void MergeListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is string item)
        {
            _draggedItem = item;
            DragDrop.DoDragDrop(lb, item, DragDropEffects.Move);
        }
    }

    private void MergeListBox_Drop(object sender, DragEventArgs e)
    {
        if (_draggedItem is null || DataContext is not OfficePdfStudioViewModel vm)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject dep)
        {
            ListBoxItem? targetContainer = FindAncestor<ListBoxItem>(dep);
            if (targetContainer?.DataContext is string targetItem)
            {
                vm.ReorderMergeItem(_draggedItem, targetItem);
            }
        }

        _draggedItem = null;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
