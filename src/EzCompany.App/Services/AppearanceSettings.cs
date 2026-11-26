namespace EzCompany.App.Services;

public sealed class AppearanceSettings
{
    public bool IsDarkMode { get; set; } = true;
    public string AccentColorHex { get; set; } = "#0078D4";
    public double BaseFontSize { get; set; } = 14.0;
    public double CardMaxWidth { get; set; } = 960.0;
    public double CardCornerRadius { get; set; } = 10.0;
    public double CardPadding { get; set; } = 16.0;
    public double SidebarWidth { get; set; } = 260.0;
    public double WindowOpacity { get; set; } = 1.0;
    public string BackdropType { get; set; } = "None";
    public double CardOpacity { get; set; } = 1.0;
    public double CardTintStrength { get; set; } = 0.08;

    public AppearanceSettings Clone() => new()
    {
        IsDarkMode = IsDarkMode,
        AccentColorHex = AccentColorHex,
        BaseFontSize = BaseFontSize,
        CardMaxWidth = CardMaxWidth,
        CardCornerRadius = CardCornerRadius,
        CardPadding = CardPadding,
        SidebarWidth = SidebarWidth,
        WindowOpacity = WindowOpacity,
        BackdropType = BackdropType,
        CardOpacity = CardOpacity,
        CardTintStrength = CardTintStrength
    };
}
