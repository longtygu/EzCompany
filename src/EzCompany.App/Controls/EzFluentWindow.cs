using EzCompany.App.Services;

namespace EzCompany.App.Controls;

/// <summary>
/// FluentWindow subclass that preserves an extended DWM glass frame (<c>GlassFrameThickness = -1</c>)
/// even when WPF-UI backdrop type is <c>None</c>, enabling true per-pixel alpha transparency and Blur/Acrylic.
/// </summary>
public class EzFluentWindow : Wpf.Ui.Controls.FluentWindow
{
    protected override void SetWindowChrome()
    {
        WindowTransparencyHelper.EnsureExtendedFrameChrome(this);
    }
}
