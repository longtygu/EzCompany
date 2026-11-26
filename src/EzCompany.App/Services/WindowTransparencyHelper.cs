using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace EzCompany.App.Services;

/// <summary>
/// Applies per-pixel DWM glass transparency, blur/acrylic/mica materials, dark frame attribute,
/// and accent tint cleanly across Windows 10 and Windows 11 without WS_EX_LAYERED glitches.
/// </summary>
public static class WindowTransparencyHelper
{
    private const int GwlExStyle = -20;
    private const nint WsExLayered = 0x00080000;

    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaMicaEffect = 1029;

    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2;      // Mica
    private const int DwmsbtTransientWindow = 3; // Acrylic
    private const int DwmsbtTabbedWindow = 4;    // Mica Alt (Tabbed)

    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    public static int OsBuild => Environment.OSVersion.Version.Build;
    public static bool IsWindows11OrGreater => OsBuild >= 22000;
    public static bool SupportsSystemBackdropAttribute => OsBuild >= 22621;
    public static bool SupportsAcrylicAccent => OsBuild >= 17134;

    public static string Apply(Window window, AppearancePalette palette, string backdropType, bool isDarkMode)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(palette);

        string normalizedBackdrop = NormalizeBackdropName(backdropType);
        window.Background = new SolidColorBrush(palette.WindowSurface);
        EnsureExtendedFrameChrome(window);

        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero)
        {
            void OnSourceInitialized(object? sender, EventArgs args)
            {
                window.SourceInitialized -= OnSourceInitialized;
                nint readyHwnd = new WindowInteropHelper(window).Handle;
                if (readyHwnd != nint.Zero)
                {
                    ApplyNative(readyHwnd, palette, normalizedBackdrop, isDarkMode);
                }
            }

            window.SourceInitialized += OnSourceInitialized;
            return DescribeEffectiveMaterial(normalizedBackdrop);
        }

        return ApplyNative(hwnd, palette, normalizedBackdrop, isDarkMode);
    }

    public static void EnsureExtendedFrameChrome(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        WindowChrome? existing = WindowChrome.GetWindowChrome(window);
        Thickness targetGlass = new(-1);
        if (existing is not null && existing.GlassFrameThickness == targetGlass)
        {
            return;
        }

        Thickness resizeBorder = (window.ResizeMode == ResizeMode.CanResize || window.ResizeMode == ResizeMode.CanResizeWithGrip)
            ? new Thickness(4)
            : default;

        WindowChrome.SetWindowChrome(
            window,
            new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = default,
                GlassFrameThickness = targetGlass,
                ResizeBorderThickness = resizeBorder,
                UseAeroCaptionButtons = false
            });
    }

    private static string ApplyNative(nint hwnd, AppearancePalette palette, string normalizedBackdrop, bool isDarkMode)
    {
        try
        {
            MARGINS margins = new()
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1
            };
            _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch
        {
        }

        try
        {
            HwndSource? source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
        }
        catch
        {
        }

        ClearLayeredStyleIfPresent(hwnd);
        ApplyDarkModeFrame(hwnd, isDarkMode);
        ApplyMaterial(hwnd, palette, normalizedBackdrop);
        return DescribeEffectiveMaterial(normalizedBackdrop);
    }

    private static void ClearLayeredStyleIfPresent(nint hwnd)
    {
        try
        {
            nint style = GetWindowLongPtr64(hwnd, GwlExStyle);
            if ((style & WsExLayered) != 0)
            {
                _ = SetWindowLongPtr64(hwnd, GwlExStyle, style & ~WsExLayered);
            }
        }
        catch
        {
        }
    }

    private static void ApplyDarkModeFrame(nint hwnd, bool isDarkMode)
    {
        if (OsBuild < 17763)
        {
            return;
        }

        int attr = OsBuild >= 18985 ? DwmwaUseImmersiveDarkMode : DwmwaUseImmersiveDarkModeBefore20H1;
        SetDwmIntAttribute(hwnd, attr, isDarkMode ? 1 : 0);
    }

    private static void ApplyMaterial(nint hwnd, AppearancePalette palette, string normalizedBackdrop)
    {
        if (string.Equals(normalizedBackdrop, "None", StringComparison.OrdinalIgnoreCase))
        {
            SetAccentPolicy(hwnd, AccentDisabled, 0);
            if (SupportsSystemBackdropAttribute)
            {
                SetDwmIntAttribute(hwnd, DwmwaSystemBackdropType, DwmsbtNone);
            }
            else if (IsWindows11OrGreater)
            {
                SetDwmIntAttribute(hwnd, DwmwaMicaEffect, 0);
            }

            return;
        }

        if (SupportsSystemBackdropAttribute)
        {
            switch (normalizedBackdrop)
            {
                case "Mica":
                    SetAccentPolicy(hwnd, AccentDisabled, 0);
                    SetDwmIntAttribute(hwnd, DwmwaSystemBackdropType, DwmsbtMainWindow);
                    break;
                case "Acrylic":
                    SetAccentPolicy(hwnd, AccentDisabled, 0);
                    SetDwmIntAttribute(hwnd, DwmwaSystemBackdropType, DwmsbtTransientWindow);
                    break;
                case "Tabbed":
                    SetAccentPolicy(hwnd, AccentDisabled, 0);
                    SetDwmIntAttribute(hwnd, DwmwaSystemBackdropType, DwmsbtTabbedWindow);
                    break;
                default: // Blur
                    SetDwmIntAttribute(hwnd, DwmwaSystemBackdropType, DwmsbtNone);
                    SetAccentPolicy(hwnd, AccentEnableBlurBehind, 0);
                    break;
            }

            return;
        }

        if (IsWindows11OrGreater && string.Equals(normalizedBackdrop, "Mica", StringComparison.OrdinalIgnoreCase))
        {
            SetAccentPolicy(hwnd, AccentDisabled, 0);
            SetDwmIntAttribute(hwnd, DwmwaMicaEffect, 1);
            return;
        }

        if (IsWindows11OrGreater)
        {
            SetDwmIntAttribute(hwnd, DwmwaMicaEffect, 0);
        }

        if (string.Equals(normalizedBackdrop, "Acrylic", StringComparison.OrdinalIgnoreCase) && SupportsAcrylicAccent)
        {
            Color surface = palette.WindowSurface;
            uint abgr = (0x10u << 24)
                | ((uint)surface.B << 16)
                | ((uint)surface.G << 8)
                | surface.R;
            SetAccentPolicy(hwnd, AccentEnableAcrylicBlurBehind, abgr);
            return;
        }

        SetAccentPolicy(hwnd, AccentEnableBlurBehind, 0);
    }

    private static void SetDwmIntAttribute(nint hwnd, int attribute, int value)
    {
        try
        {
            int v = value;
            _ = DwmSetWindowAttribute(hwnd, attribute, ref v, sizeof(int));
        }
        catch
        {
        }
    }

    private static void SetAccentPolicy(nint hwnd, int accentState, uint gradientColor)
    {
        nint accentPtr = nint.Zero;
        try
        {
            AccentPolicy policy = new()
            {
                AccentState = accentState,
                AccentFlags = accentState == AccentDisabled ? 0 : 2,
                GradientColor = gradientColor,
                AnimationId = 0
            };

            int size = Marshal.SizeOf<AccentPolicy>();
            accentPtr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(policy, accentPtr, false);

            WindowCompositionAttributeData data = new()
            {
                Attribute = WcaAccentPolicy,
                SizeOfData = size,
                Data = accentPtr
            };

            _ = SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch
        {
        }
        finally
        {
            if (accentPtr != nint.Zero)
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
    }

    private static string NormalizeBackdropName(string? backdropType)
    {
        if (string.Equals(backdropType, "Blur", StringComparison.OrdinalIgnoreCase)) return "Blur";
        if (string.Equals(backdropType, "Acrylic", StringComparison.OrdinalIgnoreCase)) return "Acrylic";
        if (string.Equals(backdropType, "Mica", StringComparison.OrdinalIgnoreCase)) return "Mica";
        if (string.Equals(backdropType, "Tabbed", StringComparison.OrdinalIgnoreCase)) return "Tabbed";
        return "None";
    }

    private static string DescribeEffectiveMaterial(string normalizedBackdrop)
    {
        if (string.Equals(normalizedBackdrop, "None", StringComparison.OrdinalIgnoreCase))
        {
            return $"Đặc (không hiệu ứng) — Windows build {OsBuild}";
        }

        if (SupportsSystemBackdropAttribute)
        {
            return normalizedBackdrop switch
            {
                "Mica" => $"Mica — Windows build {OsBuild}",
                "Acrylic" => $"Acrylic — Windows build {OsBuild}",
                "Tabbed" => $"Mica Alt (Tabbed) — Windows build {OsBuild}",
                _ => $"Kính mờ (Blur) — Windows build {OsBuild}"
            };
        }

        if (IsWindows11OrGreater)
        {
            return normalizedBackdrop switch
            {
                "Mica" => $"Mica — Windows build {OsBuild}",
                "Acrylic" => $"Acrylic — Windows build {OsBuild}",
                "Tabbed" => $"Tabbed → Kính mờ (Tabbed cần Windows 11 22H2+) — Windows build {OsBuild}",
                _ => $"Kính mờ (Blur) — Windows build {OsBuild}"
            };
        }

        return normalizedBackdrop switch
        {
            "Acrylic" when SupportsAcrylicAccent => $"Acrylic — Windows build {OsBuild}",
            "Mica" => $"Mica → Kính mờ (Mica cần Windows 11) — Windows build {OsBuild}",
            "Tabbed" => $"Mica Alt → Kính mờ (Tabbed cần Windows 11) — Windows build {OsBuild}",
            _ => $"Kính mờ (Blur) — Windows build {OsBuild}"
        };
    }
}
