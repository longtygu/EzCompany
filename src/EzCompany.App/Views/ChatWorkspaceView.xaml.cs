using System.Windows.Controls;
using EzCompany.App.ViewModels;

namespace EzCompany.App.Views;

public partial class ChatWorkspaceView : UserControl
{
    public ChatWorkspaceView(ChatWorkspaceViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        viewModel.RefreshRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(async () => await viewModel.LoadSelectedConversationMessagesAsync());
        };
        Loaded += async (_, _) => await viewModel.RefreshAllAsync();
    }
}
