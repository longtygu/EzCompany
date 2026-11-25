using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using EzCompany.Core.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace EzCompany.App.Services;

/// <summary>
/// Persists and live-applies per-profile UI appearance customization:
/// Light/Dark theme, Accent Color, Font Size, Card Width/Radius/Padding, Sidebar Width, Window Opacity &amp; Backdrop.
/// </summary>
public sealed class AppearanceSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;

    public AppearanceSettingsService(ILocalStoragePaths paths)
    {
        _settingsFilePath = Path.Combine(paths.ProfileRoot, "appearance.json");
        Current = LoadFromDisk();
    }

    public AppearanceSettings Current { get; private set; }

    public event EventHandler<AppearanceSettings>? SettingsChanged;

    public static IReadOnlyList<string> AvailableBackdrops { get; } = ["Mica", "Acrylic", "Tabbed", "None"];

    public static IReadOnlyList<(string Name, string Hex)> AccentPresets { get; } =
    [
        ("Xanh Fluent (Mặc định)", "#0078D4"),
        ("Xanh Ngọc (Teal)", "#008272"),
        ("Xanh Lá (Emerald)", "#107C10"),
        ("Tím (Iris)", "#744DA9"),
        ("Hổ Phách (Amber)", "#CA5010"),
        ("Đỏ Ruby", "#C50F1F"),
        ("Xanh Navy", "#2B579A"),
        ("Xám Thép", "#5D5A58")
    ];

    public AppearanceSettings Apply(AppearanceSettings settings, bool persist = true)
    {
        AppearanceSettings normalized = Normalize(settings);
        Current = normalized;

        if (persist)
        {
            SaveToDisk(normalized);
        }

        ApplyToWpfApplication(normalized);
        SettingsChanged?.Invoke(this, normalized);
        return normalized;
    }

    public AppearanceSettings ResetToDefaults() => Apply(new AppearanceSettings(), persist: true);

    public void ApplyToWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window is FluentWindow fluent)
        {
            fluent.WindowBackdropType = ParseBackdrop(Current.BackdropType);
        }

        WindowTransparencyHelper.ApplyOpacity(window, Current.WindowOpacity);
    }

    public static AppearanceSettings Normalize(AppearanceSettings source)
    {
        AppearanceSettings copy = source.Clone();
        copy.BaseFontSize = Math.Round(Math.Clamp(copy.BaseFontSize, 11.0, 20.0), 1);
        copy.CardMaxWidth = Math.Round(Math.Clamp(copy.CardMaxWidth, 640.0, 1600.0), 0);
        copy.CardCornerRadius = Math.Round(Math.Clamp(copy.CardCornerRadius, 4.0, 20.0), 0);
        copy.CardPadding = Math.Round(Math.Clamp(copy.CardPadding, 8.0, 28.0), 0);
        copy.SidebarWidth = Math.Round(Math.Clamp(copy.SidebarWidth, 210.0, 340.0), 0);
        copy.WindowOpacity = Math.Round(Math.Clamp(copy.WindowOpacity, 0.70, 1.00), 2);
        copy.AccentColorHex = NormalizeHex(copy.AccentColorHex);
        copy.BackdropType = AvailableBackdrops.FirstOrDefault(
            b => string.Equals(b, copy.BackdropType, StringComparison.OrdinalIgnoreCase)) ?? "Mica";
        return copy;
    }

    private void ApplyToWpfApplication(AppearanceSettings settings)
    {
        if (Application.Current is null)
        {
            return;
        }

        ApplicationTheme theme = settings.IsDarkMode ? ApplicationTheme.Dark : ApplicationTheme.Light;
        WindowBackdropType backdrop = ParseBackdrop(settings.BackdropType);
        Color accentColor = ParseColor(settings.AccentColorHex);

        ApplicationThemeManager.Apply(theme, backdrop, updateAccent: false);
        ApplicationAccentColorManager.Apply(accentColor, theme, false, true);

        ResourceDictionary res = Application.Current.Resources;
        res["AppFontSize"] = settings.BaseFontSize;
        res["AppFontSizeSmall"] = Math.Max(10.0, settings.BaseFontSize - 2.0);
        res["AppFontSizeHeading"] = settings.BaseFontSize + 4.0;
        res["AppFontSizeTitle"] = settings.BaseFontSize + 8.0;
        res["AppCardMaxWidth"] = settings.CardMaxWidth;
        res["AppCardCornerRadius"] = new CornerRadius(settings.CardCornerRadius);
        res["AppCardPadding"] = new Thickness(settings.CardPadding);
        res["AppSidebarWidth"] = new GridLength(settings.SidebarWidth);
        res["AppAccentBrush"] = new SolidColorBrush(accentColor);

        foreach (Window win in Application.Current.Windows)
        {
            if (win is FluentWindow fluent)
            {
                fluent.WindowBackdropType = backdrop;
            }

            WindowTransparencyHelper.ApplyOpacity(win, settings.WindowOpacity);
        }
    }

    private AppearanceSettings LoadFromDisk()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                AppearanceSettings? parsed = JsonSerializer.Deserialize<AppearanceSettings>(json, JsonOptions);
                if (parsed is not null)
                {
                    return Normalize(parsed);
                }
            }
        }
        catch
        {
        }

        return new AppearanceSettings();
    }

    private void SaveToDisk(AppearanceSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
        }
    }

    private static WindowBackdropType ParseBackdrop(string? name) => name switch
    {
        "Acrylic" => WindowBackdropType.Acrylic,
        "Tabbed" => WindowBackdropType.Tabbed,
        "None" => WindowBackdropType.None,
        _ => WindowBackdropType.Mica
    };

    private static string NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return "#0078D4";
        }

        string trimmed = hex.Trim();
        if (!trimmed.StartsWith('#'))
        {
            trimmed = "#" + trimmed;
        }

        try
        {
            _ = (Color)ColorConverter.ConvertFromString(trimmed);
            return trimmed.ToUpperInvariant();
        }
        catch
        {
            return "#0078D4";
        }
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Color.FromRgb(0x00, 0x78, 0xD4);
        }
    }
}
