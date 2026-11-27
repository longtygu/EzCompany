using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EzCompany.App.Services;

/// <summary>
/// Pure Win32 <c>Shell_NotifyIconW</c> + native context menu tray helper so Ez Company can
/// keep running in the background receiving LAN messages/tasks without pulling in WindowsForms.
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    private const int WmUser = 0x0400;
    private const int WmTrayIcon = WmUser + 101;
    private const int WmCommand = 0x0111;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;

    private const uint NifMessage = 0x01;
    private const uint NifIcon = 0x02;
    private const uint NifTip = 0x04;
    private const uint NifInfo = 0x10;
    private const uint NimAdd = 0x00;
    private const uint NimModify = 0x01;
    private const uint NimDelete = 0x02;

    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmReturnCmd = 0x0100;

    private const uint CmdOpenWindow = 1001;
    private const uint CmdOpenChat = 1002;
    private const uint CmdQuickCheckIn = 1003;
    private const uint CmdExitApp = 1099;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint ExtractAssociatedIconW(nint hInst, string lpIconPath, ref ushort lpiIcon);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint hMenu, uint fuFlags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandleW(string? lpModuleName);

    private HwndSource? _hwndSource;
    private nint _hWnd;
    private nint _hIcon;
    private bool _added;
    private bool _disposed;

    public event EventHandler? OpenMainWindowRequested;
    public event EventHandler? OpenChatRequested;
    public event EventHandler? QuickCheckInRequested;
    public event EventHandler? ExitRequested;

    public void Attach(Window window, string tooltip = "Ez Company — Đang kết nối LAN")
    {
        if (_disposed || _added)
        {
            return;
        }

        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        _hWnd = handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            ushort idx = 0;
            _hIcon = ExtractAssociatedIconW(GetModuleHandleW(null), exePath, ref idx);
        }

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = _hIcon,
            szTip = tooltip.Length > 120 ? tooltip[..120] : tooltip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };

        _added = Shell_NotifyIconW(NimAdd, ref data);
    }

    public void ShowBalloon(string title, string message)
    {
        if (_disposed || !_added || _hWnd == nint.Zero)
        {
            return;
        }

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NifInfo,
            szTip = string.Empty,
            szInfoTitle = title.Length > 60 ? title[..60] : title,
            szInfo = message.Length > 250 ? message[..250] : message,
            dwInfoFlags = 0x01 // NIIF_INFO
        };

        _ = Shell_NotifyIconW(NimModify, ref data);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmTrayIcon)
        {
            int mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == WmLButtonDblClk)
            {
                OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            else if (mouseMsg == WmRButtonUp)
            {
                ShowContextMenu();
                handled = true;
            }
        }
        else if (msg == WmCommand)
        {
            uint cmdId = (uint)(wParam.ToInt64() & 0xFFFF);
            HandleMenuCommand(cmdId);
        }

        return nint.Zero;
    }

    private void ShowContextMenu()
    {
        if (_hWnd == nint.Zero)
        {
            return;
        }

        nint hMenu = CreatePopupMenu();
        if (hMenu == nint.Zero)
        {
            return;
        }

        try
        {
            _ = AppendMenuW(hMenu, MfString, CmdOpenWindow, "Mở Ez Company");
            _ = AppendMenuW(hMenu, MfString, CmdOpenChat, "Mở Chat nội bộ");
            _ = AppendMenuW(hMenu, MfString, CmdQuickCheckIn, "Chấm công Hôm nay");
            _ = AppendMenuW(hMenu, MfSeparator, 0, null);
            _ = AppendMenuW(hMenu, MfString, CmdExitApp, "Thoát hoàn toàn");

            _ = GetCursorPos(out POINT pt);
            _ = SetForegroundWindow(_hWnd);
            uint selected = TrackPopupMenuEx(hMenu, TpmLeftAlign | TpmBottomAlign | TpmReturnCmd, pt.X, pt.Y, _hWnd, nint.Zero);
            HandleMenuCommand(selected);
        }
        finally
        {
            _ = DestroyMenu(hMenu);
        }
    }

    private void HandleMenuCommand(uint cmdId)
    {
        switch (cmdId)
        {
            case CmdOpenWindow:
                OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CmdOpenChat:
                OpenChatRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CmdQuickCheckIn:
                QuickCheckInRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CmdExitApp:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        if (_added && _hWnd != nint.Zero)
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = 1,
                szTip = string.Empty,
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };
            _ = Shell_NotifyIconW(NimDelete, ref data);
            _added = false;
        }

        if (_hIcon != nint.Zero)
        {
            _ = DestroyIcon(_hIcon);
            _hIcon = nint.Zero;
        }
    }
}
