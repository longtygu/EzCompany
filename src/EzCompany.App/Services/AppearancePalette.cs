using System.Windows.Media;

namespace EzCompany.App.Services;

public sealed record AppearancePalette(
    Color WindowSurface,
    Color Panel,
    Color Card,
    Color CardInner,
    Color CardBorder,
    Color Accent);

public static class AppearancePaletteBuilder
{
    public static AppearancePalette Build(AppearanceSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);

        bool dark = s.IsDarkMode;
        bool material = !string.Equals(s.BackdropType, "None", StringComparison.OrdinalIgnoreCase);
        double o = Math.Clamp(s.WindowOpacity, 0.0, 1.0);
        double cardOpacity = Math.Clamp(s.CardOpacity, 0.0, 1.0);
        double tint = Math.Clamp(s.CardTintStrength, 0.0, 1.0);
        Color accent = ParseColor(s.AccentColorHex);

        Color baseSurface = dark ? Color.FromRgb(0x1B, 0x1D, 0x22) : Color.FromRgb(0xF3, 0xF4, 0xF7);
        Color basePanel = dark ? Color.FromRgb(0x22, 0x25, 0x2C) : Color.FromRgb(0xFA, 0xFA, 0xFC);
        Color baseCard = dark ? Color.FromRgb(0x2A, 0x2D, 0x35) : Color.FromRgb(0xFF, 0xFF, 0xFF);
        Color baseInner = dark ? Color.FromRgb(0x33, 0x37, 0x3F) : Color.FromRgb(0xF2, 0xF4, 0xF8);

        double surfaceAlpha = material ? o * 0.45 : o;
        double panelAlpha = material ? o * 0.45 : o * 0.5;

        Color windowSurface = WithAlpha(Mix(baseSurface, accent, 0.04), surfaceAlpha);
        Color panel = WithAlpha(Mix(basePanel, accent, tint * 0.5), panelAlpha);
        Color card = WithAlpha(Mix(baseCard, accent, tint), cardOpacity);
        Color cardInner = WithAlpha(Mix(baseInner, accent, Math.Min(0.5, tint * 1.5)), Math.Max(0.5, cardOpacity));
        Color cardBorder = WithAlpha(
            Mix(dark ? Colors.White : Colors.Black, accent, Math.Min(1.0, tint * 4.0)),
            (dark ? 0.16 : 0.10) + tint);

        return new AppearancePalette(windowSurface, panel, card, cardInner, cardBorder, accent);
    }

    public static Color Mix(Color a, Color b, double t)
    {
        double clamped = Math.Clamp(t, 0.0, 1.0);
        byte r = (byte)Math.Round(a.R * (1.0 - clamped) + b.R * clamped);
        byte g = (byte)Math.Round(a.G * (1.0 - clamped) + b.G * clamped);
        byte bl = (byte)Math.Round(a.B * (1.0 - clamped) + b.B * clamped);
        return Color.FromArgb(255, r, g, bl);
    }

    public static Color WithAlpha(Color c, double alpha)
    {
        byte a = (byte)Math.Round(Math.Clamp(alpha, 0.0, 1.0) * 255.0);
        return Color.FromArgb(a, c.R, c.G, c.B);
    }

    public static Color ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return Color.FromRgb(0x00, 0x78, 0xD4);
        }

        string normalized = hex.Trim();
        if (!normalized.StartsWith('#'))
        {
            normalized = "#" + normalized;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(normalized);
        }
        catch
        {
            return Color.FromRgb(0x00, 0x78, 0xD4);
        }
    }
}
