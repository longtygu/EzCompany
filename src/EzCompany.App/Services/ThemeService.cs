using CommunityToolkit.Mvvm.ComponentModel;

namespace EzCompany.App.Services;

public sealed partial class ThemeService : ObservableObject
{
    private readonly AppearanceSettingsService _appearance;

    public ThemeService(AppearanceSettingsService appearance)
    {
        _appearance = appearance;
        _isDarkMode = appearance.Current.IsDarkMode;
        _appearance.SettingsChanged += (_, s) => IsDarkMode = s.IsDarkMode;
    }

    [ObservableProperty]
    private bool _isDarkMode = true;

    public void ApplyCurrentAppearance() => _appearance.Apply(_appearance.Current, persist: false);

    public void ApplyTheme(bool dark)
    {
        AppearanceSettings updated = _appearance.Current.Clone();
        updated.IsDarkMode = dark;
        _appearance.Apply(updated, persist: true);
    }

    public void ToggleTheme() => ApplyTheme(!IsDarkMode);
}
