using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace EzCompany.Modules.Legal;

public sealed partial class LegalContractDiffViewModel : ObservableObject
{
    private readonly ContractDiffService _diffService;
    private readonly ContractReportExporter _exporter;
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private ContractDiffResult? _lastResult;
    private int _currentChangeCursor = -1;

    public LegalContractDiffViewModel(
        ContractDiffService diffService,
        ContractReportExporter exporter,
        IDbContextFactory<AppDbContext> appDbFactory)
    {
        _diffService = diffService;
        _exporter = exporter;
        _appDbFactory = appDbFactory;
    }

    public ObservableCollection<SideBySideDiffLine> SideBySideLines { get; } = [];
    public ObservableCollection<InlineDiffLine> InlineLines { get; } = [];
    public ObservableCollection<LegalContractReviewEntity> SavedReviews { get; } = [];

    [ObservableProperty] private string _reviewTitle = string.Empty;
    [ObservableProperty] private string _originalPath = string.Empty;
    [ObservableProperty] private string _revisedPath = string.Empty;
    [ObservableProperty] private string _reviewerNotes = string.Empty;
    [ObservableProperty] private bool _isInlineView;
    [ObservableProperty] private string _statsSummary = "Chọn 2 tệp hợp đồng .docx để so sánh.";
    [ObservableProperty] private SideBySideDiffLine? _selectedSideBySideLine;
    [ObservableProperty] private InlineDiffLine? _selectedInlineLine;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync();
        List<LegalContractReviewEntity> list = await db.LegalContractReviews
            .AsNoTracking()
            .OrderByDescending(x => x.ReviewedAtUtc)
            .Take(100)
            .ToListAsync();

        SavedReviews.Clear();
        foreach (LegalContractReviewEntity item in list)
        {
            SavedReviews.Add(item);
        }
    }

    [RelayCommand]
    private void BrowseOriginal()
    {
        var dlg = new OpenFileDialog { Filter = "Word Document (*.docx)|*.docx", Title = "Chọn Hợp đồng Gốc" };
        if (dlg.ShowDialog() == true) OriginalPath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseRevised()
    {
        var dlg = new OpenFileDialog { Filter = "Word Document (*.docx)|*.docx", Title = "Chọn Hợp đồng Sửa đổi" };
        if (dlg.ShowDialog() == true)
        {
            RevisedPath = dlg.FileName;
            if (string.IsNullOrWhiteSpace(ReviewTitle))
            {
                ReviewTitle = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }
    }

    [RelayCommand]
    private void RunCompare()
    {
        if (!File.Exists(OriginalPath) || !File.Exists(RevisedPath))
        {
            StatusMessage = "Vui lòng chọn đầy đủ cả 2 tệp .docx hợp lệ.";
            return;
        }

        _lastResult = _diffService.CompareDocxFiles(OriginalPath, RevisedPath);
        _currentChangeCursor = -1;

        SideBySideLines.Clear();
        foreach (SideBySideDiffLine l in _lastResult.SideBySideLines) SideBySideLines.Add(l);

        InlineLines.Clear();
        foreach (InlineDiffLine l in _lastResult.InlineLines) InlineLines.Add(l);

        StatsSummary = $"Thêm: +{_lastResult.AddedCount} | Xóa: -{_lastResult.DeletedCount} | Sửa: ~{_lastResult.ModifiedCount}";
        StatusMessage = "So sánh hoàn tất offline.";
    }

    [RelayCommand]
    private void NextChange() => JumpChange(1);

    [RelayCommand]
    private void PreviousChange() => JumpChange(-1);

    [RelayCommand]
    private async Task SaveToVaultAsync()
    {
        if (_lastResult is null)
        {
            StatusMessage = "Chạy so sánh trước khi lưu vào Vault.";
            return;
        }

        LegalContractReviewEntity saved = await _exporter.SaveToVaultAndDatabaseAsync(
            ReviewTitle,
            OriginalPath,
            RevisedPath,
            _lastResult,
            ReviewerNotes);

        StatusMessage = $"Đã lưu 2 bản hợp đồng (SHA-256) vào Vault và CSDL: {saved.Title}";
        await LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task ExportHtmlAsync()
    {
        if (_lastResult is null)
        {
            StatusMessage = "Chạy so sánh trước khi xuất báo cáo.";
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "HTML Report (*.html)|*.html",
            FileName = $"ContractDiff_{DateTime.Now:yyyyMMdd}.html"
        };

        if (dlg.ShowDialog() != true) return;

        await _exporter.ExportHtmlReportAsync(
            dlg.FileName,
            ReviewTitle,
            Path.GetFileName(OriginalPath),
            Path.GetFileName(RevisedPath),
            _lastResult,
            ReviewerNotes);

        await _exporter.SaveToVaultAndDatabaseAsync(ReviewTitle, OriginalPath, RevisedPath, _lastResult, ReviewerNotes, dlg.FileName);
        StatusMessage = $"Đã xuất báo cáo HTML: {dlg.FileName}";
        await LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task ExportDocxAsync()
    {
        if (_lastResult is null)
        {
            StatusMessage = "Chạy so sánh trước khi xuất báo cáo.";
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Word Report (*.docx)|*.docx",
            FileName = $"ContractDiff_{DateTime.Now:yyyyMMdd}.docx"
        };

        if (dlg.ShowDialog() != true) return;

        await _exporter.ExportDocxReportAsync(
            dlg.FileName,
            ReviewTitle,
            Path.GetFileName(OriginalPath),
            Path.GetFileName(RevisedPath),
            _lastResult,
            ReviewerNotes);

        await _exporter.SaveToVaultAndDatabaseAsync(ReviewTitle, OriginalPath, RevisedPath, _lastResult, ReviewerNotes, dlg.FileName);
        StatusMessage = $"Đã xuất báo cáo DOCX: {dlg.FileName}";
        await LoadHistoryAsync();
    }

    private void JumpChange(int step)
    {
        if (_lastResult is null) return;
        IReadOnlyList<int> indices = IsInlineView ? _lastResult.InlineChangeIndices : _lastResult.SideBySideChangeIndices;
        if (indices.Count == 0) return;

        if (_currentChangeCursor < 0)
        {
            _currentChangeCursor = step > 0 ? 0 : indices.Count - 1;
        }
        else
        {
            _currentChangeCursor = (_currentChangeCursor + step + indices.Count) % indices.Count;
        }

        int lineIdx = indices[_currentChangeCursor];
        if (IsInlineView && lineIdx < InlineLines.Count)
        {
            SelectedInlineLine = InlineLines[lineIdx];
        }
        else if (!IsInlineView && lineIdx < SideBySideLines.Count)
        {
            SelectedSideBySideLine = SideBySideLines[lineIdx];
        }
    }
}
