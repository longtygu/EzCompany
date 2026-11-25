using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EzCompany.App.Services;

/// <summary>
/// Applies per-window opacity via Win32 <c>WS_EX_LAYERED</c> + <c>SetLayeredWindowAttributes</c>
/// without enabling WPF <c>AllowsTransparency</c>, preserving FluentWindow Mica/Acrylic and native sizing.
/// </summary>
public static class WindowTransparencyHelper
{
    private const int GwlExStyle = -20;
    private const nint WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    public static void ApplyOpacity(Window window, double opacity)
    {
        ArgumentNullException.ThrowIfNull(window);
        double clamped = Math.Clamp(opacity, 0.70, 1.0);
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero)
        {
            void OnSourceInitialized(object? sender, EventArgs args)
            {
                window.SourceInitialized -= OnSourceInitialized;
                ApplyHandleOpacity(new WindowInteropHelper(window).Handle, clamped);
            }

            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }

        ApplyHandleOpacity(hwnd, clamped);
    }

    private static void ApplyHandleOpacity(nint hwnd, double clampedOpacity)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }

        nint style = GetWindowLongPtr64(hwnd, GwlExStyle);
        if ((style & WsExLayered) == 0)
        {
            _ = SetWindowLongPtr64(hwnd, GwlExStyle, style | WsExLayered);
        }

        byte alpha = (byte)Math.Clamp((int)Math.Round(clampedOpacity * 255.0), 178, 255);
        _ = SetLayeredWindowAttributes(hwnd, 0, alpha, LwaAlpha);
    }
}
