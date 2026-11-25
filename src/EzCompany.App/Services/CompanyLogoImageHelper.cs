using System.IO;
using System.Windows.Media.Imaging;

namespace EzCompany.App.Services;

/// <summary>
/// Loads company custom logo images from bytes or falls back cleanly to the embedded application logo asset.
/// </summary>
public static class CompanyLogoImageHelper
{
    private static readonly Uri DefaultLogoUri = new("pack://application:,,,/Assets/EzCompany.png", UriKind.Absolute);

    public static BitmapImage LoadFromBytesOrDefault(byte[]? imageBytes)
    {
        if (imageBytes is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(imageBytes);
                var custom = new BitmapImage();
                custom.BeginInit();
                custom.CacheOption = BitmapCacheOption.OnLoad;
                custom.StreamSource = ms;
                custom.EndInit();
                custom.Freeze();
                return custom;
            }
            catch
            {
            }
        }

        var fallback = new BitmapImage(DefaultLogoUri);
        fallback.Freeze();
        return fallback;
    }
}
