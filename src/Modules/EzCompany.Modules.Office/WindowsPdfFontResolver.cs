using System.IO;
using PdfSharp.Fonts;

namespace EzCompany.Modules.Office;

/// <summary>
/// Thread-safe font resolver loading Windows TrueType fonts (<c>arial.ttf</c> / <c>segoeui.ttf</c>)
/// so PDFsharp 6.5 renders full Vietnamese Unicode characters offline.
/// </summary>
public sealed class WindowsPdfFontResolver : IFontResolver
{
    private static readonly object InitLock = new();
    private static bool _initialized;

    public static WindowsPdfFontResolver Instance { get; } = new();

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (InitLock)
        {
            if (_initialized) return;
            try
            {
                if (GlobalFontSettings.FontResolver is null)
                {
                    GlobalFontSettings.FontResolver = Instance;
                }
            }
            catch (InvalidOperationException)
            {
                // Already assigned in process
            }

            _initialized = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        string key = (bold, italic) switch
        {
            (true, true) => "EzArial#bi",
            (true, false) => "EzArial#b",
            (false, true) => "EzArial#i",
            _ => "EzArial#r"
        };

        return new FontResolverInfo(key);
    }

    public byte[]? GetFont(string faceName)
    {
        string fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string[] candidates = faceName switch
        {
            "EzArial#bi" => ["arialbi.ttf", "segoeuiz.ttf", "arial.ttf", "segoeui.ttf"],
            "EzArial#b" => ["arialbd.ttf", "segoeuib.ttf", "arial.ttf", "segoeui.ttf"],
            "EzArial#i" => ["ariali.ttf", "segoeuii.ttf", "arial.ttf", "segoeui.ttf"],
            _ => ["arial.ttf", "segoeui.ttf", "tahoma.ttf"]
        };

        foreach (string file in candidates)
        {
            string fullPath = Path.Combine(fontsDir, file);
            if (File.Exists(fullPath))
            {
                return File.ReadAllBytes(fullPath);
            }
        }

        throw new FileNotFoundException("Không tìm thấy phông chữ TrueType hệ thống (arial.ttf / segoeui.ttf) để nhúng PDF.");
    }
}
