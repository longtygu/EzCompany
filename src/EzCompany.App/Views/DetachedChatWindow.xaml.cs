using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EzCompany.App.Controls;
using EzCompany.App.Services;

namespace EzCompany.App.Views;

public partial class DetachedChatWindow : EzFluentWindow
{
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    private readonly AppearanceSettingsService _appearance;
    private Window? _hostWindow;
    private ChatDockCoordinator? _coordinator;
    private HwndSource? _hwndSource;
    private bool _isUserDragging;
    private bool _redockHandled;

    public DetachedChatWindow(AppearanceSettingsService appearance)
    {
        _appearance = appearance;
        InitializeComponent();
        _appearance.ApplyToWindow(this);

        SourceInitialized += (_, _) =>
        {
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(WndProcHook);
        };
        Loaded += (_, _) => _appearance.ApplyToWindow(this);
        LocationChanged += (_, _) => EvaluateDragOverlapHighlight();
        Closing += (_, _) =>
        {
            if (!_redockHandled)
            {
                _redockHandled = true;
                _coordinator?.NotifyDetachedWindowClosing();
            }
        };
        Closed += (_, _) => _hwndSource?.RemoveHook(WndProcHook);
    }

    public void AttachChat(ChatWorkspaceView chatView, Window hostWindow, ChatDockCoordinator coordinator)
    {
        _hostWindow = hostWindow;
        _coordinator = coordinator;
        _redockHandled = false;
        ChatHost.Content = chatView;
    }

    public void DetachContentForRedock()
    {
        _redockHandled = true;
        ChatHost.Content = null;
    }

    private void RedockButton_Click(object sender, RoutedEventArgs e)
    {
        _redockHandled = true;
        _coordinator?.RedockToMain();
    }

    private nint WndProcHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmEnterSizeMove)
        {
            _isUserDragging = true;
        }
        else if (msg == WmExitSizeMove)
        {
            _isUserDragging = false;
            _coordinator?.SetDropHighlight(false);
            if (IsOverlappingHostWindow())
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_redockHandled)
                    {
                        _redockHandled = true;
                        _coordinator?.RedockToMain();
                    }
                });
            }
        }

        return nint.Zero;
    }

    private void EvaluateDragOverlapHighlight()
    {
        if (!_isUserDragging || _coordinator is null)
        {
            return;
        }

        _coordinator.SetDropHighlight(IsOverlappingHostWindow());
    }

    private bool IsOverlappingHostWindow()
    {
        if (_hostWindow is null || !_hostWindow.IsVisible || _hostWindow.WindowState == WindowState.Minimized)
        {
            return false;
        }

        nint selfHwnd = new WindowInteropHelper(this).Handle;
        nint hostHwnd = new WindowInteropHelper(_hostWindow).Handle;
        if (selfHwnd == nint.Zero || hostHwnd == nint.Zero)
        {
            return false;
        }

        if (!GetWindowRect(selfHwnd, out RECT selfRect) || !GetWindowRect(hostHwnd, out RECT hostRect))
        {
            return false;
        }

        return ChatDockGeometry.ShouldRedockToMainWindow(
            new ScreenRect(selfRect.Left, selfRect.Top, selfRect.Right, selfRect.Bottom),
            new ScreenRect(hostRect.Left, hostRect.Top, hostRect.Right, hostRect.Bottom));
    }
}
