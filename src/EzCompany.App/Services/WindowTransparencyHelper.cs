using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace EzCompany.App.Services;

/// <summary>
/// Safely applies window backdrop materials and Win32 <c>WS_EX_LAYERED</c> opacity without breaking
/// Windows 10/11 color rendering or native window resize/chrome.
/// Never combines <c>WS_EX_LAYERED</c> with hardware DWM Mica/Acrylic simultaneously.
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

    public static bool IsWindows11OrGreater => Environment.OSVersion.Version.Build >= 22000;

    public static void ApplyOpacityAndBackdrop(
        Window window,
        double opacity,
        string backdropType,
        bool isDarkMode)
    {
        ArgumentNullException.ThrowIfNull(window);
        double clampedOpacity = Math.Clamp(opacity, 0.70, 1.0);
        bool useTransparentWindow = clampedOpacity < 0.995;

        // Avoid DWM tint corruption: when opacity < 100% or OS < Win11, disable DWM backdrop and paint explicit surface brush.
        WindowBackdropType requestedBackdrop = backdropType switch
        {
            "Acrylic" => WindowBackdropType.Acrylic,
            "Tabbed" => WindowBackdropType.Tabbed,
            "None" => WindowBackdropType.None,
            _ => WindowBackdropType.Mica
        };

        WindowBackdropType effectiveBackdrop = (!useTransparentWindow && IsWindows11OrGreater)
            ? requestedBackdrop
            : WindowBackdropType.None;

        if (window is FluentWindow fluent)
        {
            fluent.WindowBackdropType = effectiveBackdrop;
        }

        Color surfaceColor = ComputeWindowSurfaceColor(isDarkMode, backdropType, useTransparentWindow);
        window.Background = new SolidColorBrush(surfaceColor);

        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero)
        {
            void OnSourceInitialized(object? sender, EventArgs args)
            {
                window.SourceInitialized -= OnSourceInitialized;
                ApplyHandleOpacity(new WindowInteropHelper(window).Handle, clampedOpacity, useTransparentWindow);
            }

            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }

        ApplyHandleOpacity(hwnd, clampedOpacity, useTransparentWindow);
    }

    public static Color ComputeWindowSurfaceColor(bool isDarkMode, string? backdropType, bool forceOpaqueSurface)
    {
        if (forceOpaqueSurface || !IsWindows11OrGreater || string.Equals(backdropType, "None", StringComparison.OrdinalIgnoreCase))
        {
            return backdropType switch
            {
                "Acrylic" => isDarkMode ? Color.FromRgb(0x1E, 0x22, 0x2A) : Color.FromRgb(0xEF, 0xF3, 0xF8),
                "Tabbed" => isDarkMode ? Color.FromRgb(0x18, 0x1A, 0x20) : Color.FromRgb(0xF3, 0xF5, 0xF9),
                _ => isDarkMode ? Color.FromRgb(0x1B, 0x1D, 0x22) : Color.FromRgb(0xF6, 0xF7, 0xFA)
            };
        }

        // On Win11 with native Mica/Acrylic/Tabbed active, keep subtle translucent tint so DWM material shows cleanly
        return backdropType switch
        {
            "Acrylic" => isDarkMode ? Color.FromArgb(0xB8, 0x1C, 0x1F, 0x26) : Color.FromArgb(0xC8, 0xF2, 0xF5, 0xFA),
            "Tabbed" => isDarkMode ? Color.FromArgb(0xCC, 0x16, 0x18, 0x1E) : Color.FromArgb(0xD8, 0xF4, 0xF6, 0xFA),
            _ => isDarkMode ? Color.FromArgb(0xA8, 0x19, 0x1B, 0x20) : Color.FromArgb(0xBA, 0xF5, 0xF7, 0xFB)
        };
    }

    private static void ApplyHandleOpacity(nint hwnd, double clampedOpacity, bool enableLayered)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }

        nint style = GetWindowLongPtr64(hwnd, GwlExStyle);
        if (enableLayered)
        {
            if ((style & WsExLayered) == 0)
            {
                _ = SetWindowLongPtr64(hwnd, GwlExStyle, style | WsExLayered);
            }

            byte alpha = (byte)Math.Clamp((int)Math.Round(clampedOpacity * 255.0), 178, 255);
            _ = SetLayeredWindowAttributes(hwnd, 0, alpha, LwaAlpha);
        }
        else if ((style & WsExLayered) != 0)
        {
            _ = SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
            _ = SetWindowLongPtr64(hwnd, GwlExStyle, style & ~WsExLayered);
        }
    }
}
