using System.Windows;
using System.Windows.Controls;
using EzCompany.App.Controls;
using EzCompany.App.Services;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class CreateChatGroupWindow : EzFluentWindow
{
    private readonly List<SelectablePeerMember> _allMembers = [];

    public CreateChatGroupWindow(AppearanceSettingsService appearance)
    {
        InitializeComponent();
        appearance.ApplyToWindow(this);
        Loaded += (_, _) =>
        {
            appearance.ApplyToWindow(this);
            GroupNameTextBox.Focus();
        };
    }

    public string CreatedGroupName { get; private set; } = string.Empty;
    public IReadOnlyList<(string MachineId, string FullName)> SelectedMembers { get; private set; } = [];

    public void SetAvailableMembers(IEnumerable<ChatPeerPresenceItem> peers)
    {
        _allMembers.Clear();
        foreach (ChatPeerPresenceItem p in peers)
        {
            _allMembers.Add(new SelectablePeerMember
            {
                MachineId = p.MachineId,
                FullName = p.FullName,
                RoleDisplay = p.RoleBilingual,
                IsOnline = p.IsOnline
            });
        }

        ApplyFilter();
    }

    private void MemberFilterTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        string q = ChatWorkspaceViewModel.NormalizeForSearch(MemberFilterTextBox.Text);
        if (string.IsNullOrEmpty(q))
        {
            MembersItemsControl.ItemsSource = _allMembers;
            return;
        }

        MembersItemsControl.ItemsSource = _allMembers
            .Where(m => ChatWorkspaceViewModel.NormalizeForSearch(m.FullName).Contains(q, StringComparison.Ordinal) ||
                        ChatWorkspaceViewModel.NormalizeForSearch(m.RoleDisplay).Contains(q, StringComparison.Ordinal))
            .ToArray();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        string name = GroupNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationText.Text = "Vui lòng nhập tên nhóm chat.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        CreatedGroupName = name;
        SelectedMembers = _allMembers
            .Where(m => m.IsSelected)
            .Select(m => (m.MachineId, m.FullName))
            .ToArray();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
