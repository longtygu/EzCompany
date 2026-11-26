using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using EzCompany.App.Controls;
using EzCompany.App.Services;
using Xunit;

namespace EzCompany.Tests;

public sealed class AppearancePaletteTests
{
    [Fact]
    public void Build_WithDifferentAccentsAndTint_ProducesDifferentCardColors()
    {
        AppearancePalette red = AppearancePaletteBuilder.Build(new AppearanceSettings
        {
            IsDarkMode = true,
            AccentColorHex = "#C50F1F",
            CardTintStrength = 0.20
        });

        AppearancePalette blue = AppearancePaletteBuilder.Build(new AppearanceSettings
        {
            IsDarkMode = true,
            AccentColorHex = "#0078D4",
            CardTintStrength = 0.20
        });

        Assert.NotEqual(red.Card, blue.Card);
        Assert.True(red.Card.R > blue.Card.R);
        Assert.True(blue.Card.B > red.Card.B);
    }

    [Fact]
    public void Build_CardOpacity_ScalesCardAlpha()
    {
        AppearancePalette palette = AppearancePaletteBuilder.Build(new AppearanceSettings
        {
            CardOpacity = 0.60
        });

        Assert.InRange(palette.Card.A, (byte)152, (byte)154);
    }

    [Fact]
    public void Build_WindowOpacityWithNoneBackdrop_ScalesWindowSurfaceAlpha()
    {
        AppearancePalette palette = AppearancePaletteBuilder.Build(new AppearanceSettings
        {
            WindowOpacity = 0.50,
            BackdropType = "None"
        });

        Assert.InRange(palette.WindowSurface.A, (byte)126, (byte)129);
    }

    [Fact]
    public void Build_MicaBackdropAtFullOpacity_KeepsTranslucentWindowSurface()
    {
        AppearancePalette palette = AppearancePaletteBuilder.Build(new AppearanceSettings
        {
            WindowOpacity = 1.00,
            BackdropType = "Mica"
        });

        Assert.True(palette.WindowSurface.A < 255);
    }

    [Fact]
    public void Build_ZeroTintInDarkMode_PreservesExactBaseCardRgb()
    {
        AppearancePalette palette = AppearancePaletteBuilder.Build(new AppearanceSettings
        {
            IsDarkMode = true,
            AccentColorHex = "#C50F1F",
            CardTintStrength = 0.00
        });

        Assert.Equal(Color.FromRgb(0x2A, 0x2D, 0x35).R, palette.Card.R);
        Assert.Equal(Color.FromRgb(0x2A, 0x2D, 0x35).G, palette.Card.G);
        Assert.Equal(Color.FromRgb(0x2A, 0x2D, 0x35).B, palette.Card.B);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [Fact]
    public void EzFluentWindow_And_WindowTransparencyHelper_ConfigureExtendedGlassAndClearLayeredStyle()
    {
        Exception? threadEx = null;
        string blurMaterial = string.Empty;
        string noneMaterial = string.Empty;
        Color compBg = default;
        Thickness glass = default;
        nint exStyle = 0;
        byte blurSurfaceAlpha = 0;
        byte noneSurfaceAlpha = 0;

        Thread sta = new(() =>
        {
            try
            {
                var window = new EzFluentWindow
                {
                    Width = 400,
                    Height = 300,
                    Left = -3200,
                    Top = -3200,
                    ShowInTaskbar = false,
                    ShowActivated = false
                };

                window.Show();

                AppearancePalette blurPalette = AppearancePaletteBuilder.Build(new AppearanceSettings
                {
                    IsDarkMode = true,
                    AccentColorHex = "#C50F1F",
                    WindowOpacity = 0.40,
                    BackdropType = "Blur",
                    CardTintStrength = 0.25
                });
                blurMaterial = WindowTransparencyHelper.Apply(window, blurPalette, "Blur", isDarkMode: true);
                blurSurfaceAlpha = ((SolidColorBrush)window.Background).Color.A;

                nint hwnd = new WindowInteropHelper(window).Handle;
                HwndSource? hs = HwndSource.FromHwnd(hwnd);
                Assert.NotNull(hs?.CompositionTarget);
                compBg = hs!.CompositionTarget!.BackgroundColor;
                glass = WindowChrome.GetWindowChrome(window).GlassFrameThickness;
                exStyle = GetWindowLongPtr64(hwnd, -20);

                AppearancePalette nonePalette = AppearancePaletteBuilder.Build(new AppearanceSettings
                {
                    IsDarkMode = true,
                    AccentColorHex = "#C50F1F",
                    WindowOpacity = 0.40,
                    BackdropType = "None"
                });
                noneMaterial = WindowTransparencyHelper.Apply(window, nonePalette, "None", isDarkMode: true);
                noneSurfaceAlpha = ((SolidColorBrush)window.Background).Color.A;

                window.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
        Assert.Equal(Colors.Transparent, compBg);
        Assert.Equal(new Thickness(-1), glass);
        Assert.Equal(0, exStyle & 0x00080000);
        Assert.InRange(blurSurfaceAlpha, (byte)45, (byte)47);
        Assert.InRange(noneSurfaceAlpha, (byte)101, (byte)103);
        Assert.Contains("Blur", blurMaterial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Đặc", noneMaterial, StringComparison.OrdinalIgnoreCase);
    }
}
