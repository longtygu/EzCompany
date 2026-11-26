using System.Windows;
using EzCompany.App.Views;
using EzCompany.Core.Abstractions;

namespace EzCompany.App.Services;

/// <summary>
/// Listens for incoming <see cref="ChatMessageReceivedEvent"/> notifications and displays
/// non-intrusive bottom-right floating popup cards across the workstation desktop.
/// </summary>
public sealed class ChatNotificationPopupService : IDisposable
{
    private const int MaxVisiblePopups = 3;

    private readonly ILocalStoragePaths _paths;
    private readonly IDisposable _subscription;
    private readonly List<ChatToastPopupWindow> _activePopups = [];

    public ChatNotificationPopupService(IEventBus eventBus, ILocalStoragePaths paths)
    {
        _paths = paths;
        _subscription = eventBus.Subscribe<ChatMessageReceivedEvent>(OnChatMessageReceived);
    }

    public bool IsChatViewActive { get; set; }
    public Guid? ActiveViewingConversationId { get; set; }

    public event EventHandler<Guid>? OpenChatConversationRequested;

    public void CloseAllPopups()
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (ChatToastPopupWindow popup in _activePopups.ToArray())
            {
                try
                {
                    popup.Close();
                }
                catch
                {
                }
            }
            _activePopups.Clear();
        });
    }

    private void OnChatMessageReceived(ChatMessageReceivedEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.SenderMachineId) &&
            string.Equals(evt.SenderMachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.Dispatcher.BeginInvoke(() =>
        {
            bool mainWindowFocused = app.MainWindow?.IsActive == true;
            if (IsChatViewActive && ActiveViewingConversationId == evt.ConversationId && mainWindowFocused)
            {
                return;
            }

            if (_activePopups.Count >= MaxVisiblePopups)
            {
                ChatToastPopupWindow oldest = _activePopups[0];
                _activePopups.RemoveAt(0);
                oldest.Close();
            }

            var toast = new ChatToastPopupWindow(evt.ConversationId, evt.SenderFullName, evt.Preview);
            toast.ToastClicked += (_, convId) => OpenChatConversationRequested?.Invoke(this, convId);
            toast.Closed += (_, _) =>
            {
                _activePopups.Remove(toast);
                RepositionPopups();
            };

            _activePopups.Add(toast);
            toast.Show();
            toast.UpdateLayout();
            RepositionPopups();
        });
    }

    private void RepositionPopups()
    {
        Rect area = SystemParameters.WorkArea;
        double currentBottom = area.Bottom - 14;
        const double gap = 8;

        for (int i = _activePopups.Count - 1; i >= 0; i--)
        {
            ChatToastPopupWindow w = _activePopups[i];
            double h = w.ActualHeight > 10 ? w.ActualHeight : 96;
            w.Left = area.Right - w.Width - 14;
            w.Top = currentBottom - h;
            currentBottom = w.Top - gap;
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        CloseAllPopups();
    }
}
