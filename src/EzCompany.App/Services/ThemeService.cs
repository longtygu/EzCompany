using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace EzCompany.App.Services;

public sealed partial class ThemeService : ObservableObject
{
    [ObservableProperty]
    private bool _isDarkMode = true;

    public void ApplyTheme(bool dark)
    {
        IsDarkMode = dark;
        ApplicationThemeManager.Apply(
            dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            updateAccent: true);
    }

    public void ToggleTheme() => ApplyTheme(!IsDarkMode);
}
