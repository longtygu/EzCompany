using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace EzCompany.Modules.Office;

public sealed partial class OfficePdfStudioViewModel : ObservableObject
{
    private readonly LocalPdfStudioService _pdfService;

    public OfficePdfStudioViewModel(LocalPdfStudioService pdfService)
    {
        _pdfService = pdfService;
    }

    public ObservableCollection<string> MergeFiles { get; } = [];
    public ObservableCollection<string> ImageToPdfFiles { get; } = [];
    public IReadOnlyList<int> RotationAngles { get; } = [90, 180, 270];

    [ObservableProperty] private string? _selectedMergeFile;
    [ObservableProperty] private bool _saveToVault = true;
    [ObservableProperty] private string _statusMessage = "PDF Studio Offline (PDFsharp + PdfPig). Hỗ trợ lưu nhanh vào Vault/Office/.";

    // Split
    [ObservableProperty] private string _splitInputPath = string.Empty;
    [ObservableProperty] private string _splitRanges = "1-2";
    [ObservableProperty] private string _splitInputInfo = string.Empty;

    // Rotate
    [ObservableProperty] private string _rotateInputPath = string.Empty;
    [ObservableProperty] private int _rotateDegrees = 90;
    [ObservableProperty] private string _rotateRanges = string.Empty;

    // Watermark
    [ObservableProperty] private string _watermarkInputPath = string.Empty;
    [ObservableProperty] private string _watermarkText = "TÀI LIỆU NỘI BỘ";
    [ObservableProperty] private bool _watermarkIncludeTimestamp = true;
    [ObservableProperty] private bool _watermarkIncludeUsername = true;
    [ObservableProperty] private double _watermarkOpacity = 0.25;
    [ObservableProperty] private double _watermarkFontSize = 36;
    [ObservableProperty] private string _watermarkColorHex = "#B00020";

    // Images -> PDF
    [ObservableProperty] private string? _selectedImageToPdfFile;
    [ObservableProperty] private bool _imageFitToA4 = true;

    // Stamp signature / seal
    [ObservableProperty] private string _stampInputPdfPath = string.Empty;
    [ObservableProperty] private string _stampImagePath = string.Empty;
    [ObservableProperty] private int _stampPageNumber = 1;
    [ObservableProperty] private double _stampXPercent = 65.0;
    [ObservableProperty] private double _stampYPercent = 72.0;
    [ObservableProperty] private double _stampWidthPercent = 20.0;

    [RelayCommand]
    private void AddMergeFiles()
    {
        var dlg = new OpenFileDialog { Filter = "PDF Files (*.pdf)|*.pdf", Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        foreach (string f in dlg.FileNames)
        {
            MergeFiles.Add(f);
        }
    }

    [RelayCommand]
    private void MoveMergeUp()
    {
        if (SelectedMergeFile is null) return;
        int idx = MergeFiles.IndexOf(SelectedMergeFile);
        if (idx > 0)
        {
            MergeFiles.Move(idx, idx - 1);
        }
    }

    [RelayCommand]
    private void MoveMergeDown()
    {
        if (SelectedMergeFile is null) return;
        int idx = MergeFiles.IndexOf(SelectedMergeFile);
        if (idx >= 0 && idx < MergeFiles.Count - 1)
        {
            MergeFiles.Move(idx, idx + 1);
        }
    }

    [RelayCommand]
    private void RemoveSelectedMergeFile()
    {
        if (SelectedMergeFile is not null)
        {
            MergeFiles.Remove(SelectedMergeFile);
        }
    }

    public void ReorderMergeItem(string draggedItem, string targetItem)
    {
        int oldIndex = MergeFiles.IndexOf(draggedItem);
        int newIndex = MergeFiles.IndexOf(targetItem);
        if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
        {
            MergeFiles.Move(oldIndex, newIndex);
        }
    }

    [RelayCommand]
    private async Task ExecuteMergeAsync()
    {
        if (MergeFiles.Count == 0)
        {
            StatusMessage = "Thêm ít nhất 1 tệp PDF để gộp.";
            return;
        }

        string? outPath = PromptSavePdf("MergedDocument.pdf");
        if (outPath is null) return;

        PdfOperationResult res = await _pdfService.MergePdfsAsync(MergeFiles.ToList(), outPath, SaveToVault);
        StatusMessage = $"Đã gộp {res.PageCount} trang -> {res.OutputPath}" + (res.VaultRelativePath is null ? "" : $" (Vault: {res.VaultRelativePath})");
    }

    [RelayCommand]
    private void BrowseSplitInput()
    {
        string? path = PromptOpenPdf();
        if (path is null) return;
        SplitInputPath = path;
        PdfInspectionResult info = _pdfService.InspectPdf(path);
        SplitInputInfo = $"Tổng số trang: {info.PageCount}";
    }

    [RelayCommand]
    private async Task ExecuteSplitAsync()
    {
        if (!File.Exists(SplitInputPath))
        {
            StatusMessage = "Chọn tệp PDF cần tách.";
            return;
        }

        string? outPath = PromptSavePdf($"Split_{Path.GetFileName(SplitInputPath)}");
        if (outPath is null) return;

        try
        {
            PdfOperationResult res = await _pdfService.SplitPdfAsync(SplitInputPath, SplitRanges, outPath, SaveToVault);
            StatusMessage = $"Đã tách {res.PageCount} trang -> {res.OutputPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tách trang: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseRotateInput()
    {
        string? path = PromptOpenPdf();
        if (path is not null) RotateInputPath = path;
    }

    [RelayCommand]
    private async Task ExecuteRotateAsync()
    {
        if (!File.Exists(RotateInputPath))
        {
            StatusMessage = "Chọn tệp PDF cần xoay.";
            return;
        }

        string? outPath = PromptSavePdf($"Rotated_{Path.GetFileName(RotateInputPath)}");
        if (outPath is null) return;

        PdfOperationResult res = await _pdfService.RotatePdfAsync(RotateInputPath, RotateDegrees, RotateRanges, outPath, SaveToVault);
        StatusMessage = $"Đã xoay {RotateDegrees}° ({res.PageCount} trang) -> {res.OutputPath}";
    }

    [RelayCommand]
    private void BrowseWatermarkInput()
    {
        string? path = PromptOpenPdf();
        if (path is not null) WatermarkInputPath = path;
    }

    [RelayCommand]
    private async Task ExecuteWatermarkAsync()
    {
        if (!File.Exists(WatermarkInputPath))
        {
            StatusMessage = "Chọn tệp PDF cần đóng dấu Watermark.";
            return;
        }

        string? outPath = PromptSavePdf($"Watermarked_{Path.GetFileName(WatermarkInputPath)}");
        if (outPath is null) return;

        var opts = new PdfWatermarkOptions(
            WatermarkText,
            WatermarkIncludeTimestamp,
            WatermarkIncludeUsername,
            WatermarkOpacity,
            WatermarkFontSize,
            WatermarkColorHex);

        PdfOperationResult res = await _pdfService.WatermarkPdfAsync(WatermarkInputPath, opts, outPath, SaveToVault);
        StatusMessage = $"Đã đóng dấu Watermark ({res.PageCount} trang) -> {res.OutputPath}";
    }

    [RelayCommand]
    private void AddImagesToPdf()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Chọn ảnh chuyển thành PDF",
            Filter = "Hình ảnh (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (string f in dlg.FileNames)
        {
            ImageToPdfFiles.Add(f);
        }
    }

    [RelayCommand]
    private void RemoveSelectedImageToPdf()
    {
        if (SelectedImageToPdfFile is not null)
        {
            ImageToPdfFiles.Remove(SelectedImageToPdfFile);
        }
    }

    [RelayCommand]
    private async Task ExecuteImagesToPdfAsync()
    {
        if (ImageToPdfFiles.Count == 0)
        {
            StatusMessage = "Thêm ít nhất 1 tệp ảnh để chuyển thành PDF.";
            return;
        }

        string? outPath = PromptSavePdf($"ImagesToPdf_{DateTime.Now:yyyyMMdd}.pdf");
        if (outPath is null) return;

        PdfOperationResult res = await _pdfService.ConvertImagesToPdfAsync(ImageToPdfFiles.ToList(), outPath, ImageFitToA4, SaveToVault);
        StatusMessage = $"Đã chuyển {res.PageCount} ảnh thành PDF -> {res.OutputPath}";
    }

    [RelayCommand]
    private void BrowseStampPdf()
    {
        string? path = PromptOpenPdf();
        if (path is not null) StampInputPdfPath = path;
    }

    [RelayCommand]
    private void BrowseStampImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Chọn ảnh chữ ký / con dấu (PNG nền trong suốt)",
            Filter = "Hình ảnh (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        if (dlg.ShowDialog() == true) StampImagePath = dlg.FileName;
    }

    [RelayCommand]
    private async Task ExecuteStampImageAsync()
    {
        if (!File.Exists(StampInputPdfPath) || !File.Exists(StampImagePath))
        {
            StatusMessage = "Vui lòng chọn đầy đủ tệp PDF và ảnh chữ ký / con dấu.";
            return;
        }

        string? outPath = PromptSavePdf($"Signed_{Path.GetFileName(StampInputPdfPath)}");
        if (outPath is null) return;

        try
        {
            PdfOperationResult res = await _pdfService.StampImageOnPdfAsync(
                StampInputPdfPath, StampImagePath, outPath,
                StampPageNumber, StampXPercent, StampYPercent, StampWidthPercent, SaveToVault);
            StatusMessage = $"Đã chèn ảnh chữ ký/con dấu trang {StampPageNumber} -> {res.OutputPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi chèn chữ ký/con dấu: {ex.Message}";
        }
    }

    private static string? PromptOpenPdf()
    {
        var dlg = new OpenFileDialog { Filter = "PDF Files (*.pdf)|*.pdf" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static string? PromptSavePdf(string defaultName)
    {
        var dlg = new SaveFileDialog { Filter = "PDF Files (*.pdf)|*.pdf", FileName = defaultName };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
