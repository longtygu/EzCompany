using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace EzCompany.App.Views;

public partial class ChatToastPopupWindow : Window
{
    private readonly Guid _conversationId;
    private readonly DispatcherTimer _autoCloseTimer;

    public ChatToastPopupWindow(Guid conversationId, string senderFullName, string preview)
    {
        _conversationId = conversationId;
        InitializeComponent();

        SenderNameText.Text = senderFullName;
        PreviewText.Text = preview;
        InitialsText.Text = BuildInitials(senderFullName);

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            Close();
        };
        Loaded += (_, _) => _autoCloseTimer.Start();
        Closed += (_, _) => _autoCloseTimer.Stop();
    }

    public event EventHandler<Guid>? ToastClicked;

    private void Root_MouseEnter(object sender, MouseEventArgs e) => _autoCloseTimer.Stop();

    private void Root_MouseLeave(object sender, MouseEventArgs e) => _autoCloseTimer.Start();

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ToastClicked?.Invoke(this, _conversationId);
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private static string BuildInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "EZ";
        }

        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
        }

        return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
    }
}
