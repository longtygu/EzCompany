using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed partial class SnapshotViewerViewModel : ObservableObject
{
    private readonly IReadOnlyList<SnapshotTimelineItem> _items;
    private readonly Func<SnapshotTimelineItem, Task<byte[]>> _fullLoader;
    private readonly string _exportDirectory;
    private byte[]? _currentFullJpeg;
    private int _loadGeneration;

    public SnapshotViewerViewModel(
        IReadOnlyList<SnapshotTimelineItem> items,
        int startIndex,
        Func<SnapshotTimelineItem, Task<byte[]>> fullLoader,
        string exportDirectory)
    {
        _items = items ?? [];
        _fullLoader = fullLoader ?? throw new ArgumentNullException(nameof(fullLoader));
        _exportDirectory = exportDirectory ?? string.Empty;
        _currentIndex = _items.Count == 0 ? 0 : Math.Clamp(startIndex, 0, _items.Count - 1);
        _zoomPercent = 100.0;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private int _currentIndex;

    [ObservableProperty] private BitmapSource? _currentImage;
    [ObservableProperty] private string _caption = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomScale))]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    private double _zoomPercent = 100.0;

    [ObservableProperty] private string _statusText = string.Empty;

    public double ZoomScale => ZoomPercent / 100.0;
    public string ZoomText => $"{ZoomPercent:F0}%";
    public string PositionText => _items.Count == 0 ? "0/0" : $"{CurrentIndex + 1}/{_items.Count}";

    public event EventHandler? FitToWindowRequested;

    partial void OnZoomPercentChanged(double value)
    {
        double clamped = ClampZoom(value);
        if (Math.Abs(clamped - value) > 0.001)
        {
            ZoomPercent = clamped;
        }
    }

    public async Task LoadCurrentAsync()
    {
        if (_items.Count == 0 || CurrentIndex < 0 || CurrentIndex >= _items.Count)
        {
            CurrentImage = null;
            Caption = "Không có ảnh chụp nào.";
            StatusText = string.Empty;
            return;
        }

        int generation = Interlocked.Increment(ref _loadGeneration);
        SnapshotTimelineItem item = _items[CurrentIndex];
        _currentFullJpeg = null;
        Caption = item.Caption;
        CurrentImage = item.ThumbnailImage;
        StatusText = "Đang giải mã ảnh rõ nét...";

        try
        {
            byte[] jpeg = await _fullLoader(item);
            if (generation != _loadGeneration)
            {
                return;
            }

            _currentFullJpeg = jpeg;
            BitmapImage bmp = DecodeJpeg(jpeg);
            CurrentImage = bmp;
            StatusText = $"Ảnh rõ nét {bmp.PixelWidth}×{bmp.PixelHeight}px";
            FitToWindowRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration)
            {
                return;
            }

            StatusText = $"Chỉ có ảnh thu nhỏ: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PreviousAsync()
    {
        if (_items.Count == 0 || CurrentIndex <= 0)
        {
            return;
        }

        CurrentIndex--;
        await LoadCurrentAsync();
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (_items.Count == 0 || CurrentIndex >= _items.Count - 1)
        {
            return;
        }

        CurrentIndex++;
        await LoadCurrentAsync();
    }

    [RelayCommand]
    private void ZoomIn() => ZoomPercent = ZoomStepIn(ZoomPercent);

    [RelayCommand]
    private void ZoomOut() => ZoomPercent = ZoomStepOut(ZoomPercent);

    [RelayCommand]
    private void ResetZoom() => ZoomPercent = 100.0;

    [RelayCommand]
    private void FitToWindow() => FitToWindowRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (_items.Count == 0 || CurrentIndex < 0 || CurrentIndex >= _items.Count)
        {
            return;
        }

        SnapshotTimelineItem item = _items[CurrentIndex];
        byte[]? bytes = _currentFullJpeg ?? EncodeBitmapToJpeg(CurrentImage);
        if (bytes is null || bytes.Length == 0)
        {
            StatusText = "Chưa có dữ liệu ảnh để lưu.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Lưu ảnh chụp màn hình",
            Filter = "Ảnh JPEG (*.jpg)|*.jpg",
            FileName = BuildExportFileName(item)
        };

        if (dialog.ShowDialog() == true)
        {
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            StatusText = $"Đã lưu ảnh: {dialog.FileName}";
        }
    }

    [RelayCommand]
    private async Task OpenExternalAsync()
    {
        if (_items.Count == 0 || CurrentIndex < 0 || CurrentIndex >= _items.Count)
        {
            return;
        }

        SnapshotTimelineItem item = _items[CurrentIndex];
        byte[]? bytes = _currentFullJpeg ?? EncodeBitmapToJpeg(CurrentImage);
        if (bytes is null || bytes.Length == 0)
        {
            StatusText = "Chưa có dữ liệu ảnh để mở.";
            return;
        }

        try
        {
            Directory.CreateDirectory(_exportDirectory);
            string destPath = Path.Combine(_exportDirectory, BuildExportFileName(item));
            await File.WriteAllBytesAsync(destPath, bytes);
            Process.Start(new ProcessStartInfo(destPath)
            {
                UseShellExecute = true
            });
            StatusText = $"Đã mở bằng ứng dụng xem ảnh mặc định: {Path.GetFileName(destPath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Không thể mở ảnh bằng ứng dụng ngoài: {ex.Message}";
        }
    }

    public static double ClampZoom(double percent) => Math.Clamp(percent, 10.0, 800.0);

    public static double ZoomStepIn(double percent) => ClampZoom(Math.Round(percent * 1.25));

    public static double ZoomStepOut(double percent) => ClampZoom(Math.Round(percent / 1.25));

    public static double ComputeFitZoom(double viewportWidth, double viewportHeight, double imageWidth, double imageHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return 100.0;
        }

        double ratio = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
        return Math.Clamp(Math.Floor(ratio * 100.0), 10.0, 100.0);
    }

    public static string BuildExportFileName(SnapshotTimelineItem item)
    {
        string rawUser = string.IsNullOrWhiteSpace(item.Username) ? "snapshot" : item.Username.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] sanitized = rawUser.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray();
        string safeUser = new string(sanitized).Trim('_');
        if (string.IsNullOrWhiteSpace(safeUser))
        {
            safeUser = "snapshot";
        }

        return $"{safeUser}_{item.CapturedAtUtc.ToLocalTime():yyyyMMdd_HHmmss}.jpg";
    }

    private static BitmapImage DecodeJpeg(byte[] jpegBytes)
    {
        using var ms = new MemoryStream(jpegBytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static byte[]? EncodeBitmapToJpeg(BitmapSource? source)
    {
        if (source is null)
        {
            return null;
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
