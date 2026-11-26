using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EzCompany.Core.Abstractions;

namespace EzCompany.App.Services;

public sealed record CustomStickerItem(
    Guid Id,
    string Name,
    string FileName,
    string FullPath,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Manages local custom chat sticker packs stored under <c>Vault\Chat\Stickers\</c>.
/// Generates crisp built-in starter stickers on first UI run and supports adding/deleting user image stickers.
/// </summary>
public sealed class CustomStickerService
{
    public const long MaxStickerBytes = 2 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _stickersDir;
    private readonly string _indexFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CustomStickerService(ILocalStoragePaths paths)
    {
        _stickersDir = Path.Combine(paths.VaultDirectory, "Chat", "Stickers");
        Directory.CreateDirectory(_stickersDir);
        _indexFilePath = Path.Combine(_stickersDir, "stickers.json");
    }

    public IReadOnlyList<CustomStickerItem> GetStickers()
    {
        _lock.Wait();
        try
        {
            List<CustomStickerItem> items = LoadIndexUnsafe();
            items = items.Where(x => File.Exists(x.FullPath)).ToList();

            if (items.Count == 0 && !File.Exists(_indexFilePath))
            {
                TrySeedStarterStickersUnsafe(items);
                SaveIndexUnsafe(items);
            }

            return items.OrderBy(x => x.CreatedAtUtc).ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CustomStickerItem> AddStickerAsync(
        string sourceImagePath,
        string? displayName = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImagePath);
        if (!File.Exists(sourceImagePath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp ảnh sticker.", sourceImagePath);
        }

        string ext = Path.GetExtension(sourceImagePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Sticker chỉ hỗ trợ định dạng PNG, JPG, GIF, BMP hoặc WebP.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(sourceImagePath, ct).ConfigureAwait(false);
        if (bytes.Length == 0 || bytes.Length > MaxStickerBytes)
        {
            throw new InvalidOperationException("Kích thước ảnh sticker phải từ 1 byte đến tối đa 2 MB.");
        }

        Guid id = Guid.NewGuid();
        string originalName = Path.GetFileNameWithoutExtension(sourceImagePath);
        string cleanLabel = string.IsNullOrWhiteSpace(displayName) ? originalName : displayName.Trim();
        if (string.IsNullOrWhiteSpace(cleanLabel))
        {
            cleanLabel = "Sticker";
        }

        string safeFile = SanitizeFileName(Path.GetFileName(sourceImagePath));
        string storedFileName = $"{id:N}_{safeFile}";
        string destPath = Path.Combine(_stickersDir, storedFileName);
        Directory.CreateDirectory(_stickersDir);
        await File.WriteAllBytesAsync(destPath, bytes, ct).ConfigureAwait(false);

        var created = new CustomStickerItem(id, cleanLabel, safeFile, destPath, DateTimeOffset.UtcNow);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<CustomStickerItem> items = LoadIndexUnsafe()
                .Where(x => File.Exists(x.FullPath))
                .ToList();
            items.Add(created);
            SaveIndexUnsafe(items);
        }
        finally
        {
            _lock.Release();
        }

        return created;
    }

    public async Task DeleteStickerAsync(Guid stickerId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<CustomStickerItem> items = LoadIndexUnsafe();
            CustomStickerItem? target = items.FirstOrDefault(x => x.Id == stickerId);
            if (target is null)
            {
                return;
            }

            items.Remove(target);
            SaveIndexUnsafe(items);

            if (File.Exists(target.FullPath))
            {
                File.Delete(target.FullPath);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public static BitmapImage? LoadBitmapFrozen(string? filePath, int decodePixelWidth = 160)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodePixelWidth;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private List<CustomStickerItem> LoadIndexUnsafe()
    {
        try
        {
            if (File.Exists(_indexFilePath))
            {
                string json = File.ReadAllText(_indexFilePath);
                return JsonSerializer.Deserialize<List<CustomStickerItem>>(json, JsonOptions) ?? [];
            }
        }
        catch
        {
        }

        return [];
    }

    private void SaveIndexUnsafe(List<CustomStickerItem> items)
    {
        Directory.CreateDirectory(_stickersDir);
        string json = JsonSerializer.Serialize(items, JsonOptions);
        File.WriteAllText(_indexFilePath, json);
    }

    private void TrySeedStarterStickersUnsafe(List<CustomStickerItem> items)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            return;
        }

        (string Name, string Caption, string BgHex, string AccentHex)[] presets =
        [
            ("OK Sếp!", "OK!", "#0078D4", "#FFFFFF"),
            ("Đã xong", "DONE ✓", "#107C10", "#FFFFFF"),
            ("Đang làm", "WIP...", "#CA5010", "#FFFFFF"),
            ("Cảm ơn", "THANKS!", "#744DA9", "#FFFFFF")
        ];

        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i < presets.Length; i++)
        {
            (string name, string caption, string bgHex, string fgHex) = presets[i];
            Guid id = Guid.NewGuid();
            string fileName = $"starter_{i + 1}.png";
            string destPath = Path.Combine(_stickersDir, fileName);

            try
            {
                RenderBadgeStickerPng(destPath, caption, bgHex, fgHex);
                items.Add(new CustomStickerItem(id, name, fileName, destPath, now.AddMilliseconds(i)));
            }
            catch
            {
            }
        }
    }

    private static void RenderBadgeStickerPng(string outputPath, string caption, string bgHex, string fgHex)
    {
        const int size = 160;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
            var fgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex));
            var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 4);

            dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(10, 26, size - 20, size - 52), 26, 26);

            var formatted = new FormattedText(
                caption,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                28,
                fgBrush,
                1.0);

            double textX = (size - formatted.Width) / 2.0;
            double textY = (size - formatted.Height) / 2.0;
            dc.DrawText(formatted, new Point(textX, textY));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(fs);
    }

    private static string SanitizeFileName(string fileName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(fileName.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "sticker.png" : cleaned;
    }
}
