using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace EzCompany.Modules.Legal;

public sealed partial class LegalContractDiffViewModel : ObservableObject
{
    private readonly ContractDiffService _diffService;
    private readonly ContractReportExporter _exporter;
    private readonly ContractRegistryService _contractRegistry;
    private readonly ContractRegistryExcelExporter _contractExcel;
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private ContractDiffResult? _lastResult;
    private int _currentChangeCursor = -1;

    public LegalContractDiffViewModel(
        ContractDiffService diffService,
        ContractReportExporter exporter,
        ContractRegistryService contractRegistry,
        ContractRegistryExcelExporter contractExcel,
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session)
    {
        _diffService = diffService;
        _exporter = exporter;
        _contractRegistry = contractRegistry;
        _contractExcel = contractExcel;
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
    }

    public ObservableCollection<SideBySideDiffLine> SideBySideLines { get; } = [];
    public ObservableCollection<InlineDiffLine> InlineLines { get; } = [];
    public ObservableCollection<LegalContractReviewEntity> SavedReviews { get; } = [];
    public ObservableCollection<ContractRecordEntity> Contracts { get; } = [];

    public IReadOnlyList<string> ContractTypes { get; } = ["Customer", "Vendor", "Labor", "NDA", "Lease", "Other"];
    public IReadOnlyList<string> ContractStatusFilters { get; } = ["ALL", "Active", "ExpiringSoon", "Expired", "Terminated"];

    [ObservableProperty] private string _reviewTitle = string.Empty;
    [ObservableProperty] private string _originalPath = string.Empty;
    [ObservableProperty] private string _revisedPath = string.Empty;
    [ObservableProperty] private string _reviewerNotes = string.Empty;
    [ObservableProperty] private bool _isInlineView;
    [ObservableProperty] private string _statsSummary = "Chọn 2 tệp hợp đồng .docx để so sánh.";
    [ObservableProperty] private SideBySideDiffLine? _selectedSideBySideLine;
    [ObservableProperty] private InlineDiffLine? _selectedInlineLine;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // Tab 2: Contract Registry
    [ObservableProperty] private string _contractSearchQuery = string.Empty;
    [ObservableProperty] private string _contractStatusFilter = "ALL";
    [ObservableProperty] private ContractRecordEntity? _selectedContract;
    [ObservableProperty] private int _totalContractsCount;
    [ObservableProperty] private int _expiringSoonContractsCount;
    [ObservableProperty] private int _expiredContractsCount;

    [ObservableProperty] private Guid _editingContractId;
    [ObservableProperty] private string _editContractNumber = string.Empty;
    [ObservableProperty] private string _editContractTitle = string.Empty;
    [ObservableProperty] private string _editCounterpartyName = string.Empty;
    [ObservableProperty] private string _editContractType = "Customer";
    [ObservableProperty] private DateTime _editSignedDate = DateTime.Today;
    [ObservableProperty] private DateTime _editStartDate = DateTime.Today;
    [ObservableProperty] private DateTime _editExpiryDate = DateTime.Today.AddYears(1);
    [ObservableProperty] private string _editValueVndText = "0";
    [ObservableProperty] private string _editContractStatus = "Active";
    [ObservableProperty] private string _editContractAttachPath = string.Empty;
    [ObservableProperty] private string _editContractNotes = string.Empty;

    partial void OnContractSearchQueryChanged(string value) => _ = RefreshContractsAsync();
    partial void OnContractStatusFilterChanged(string value) => _ = RefreshContractsAsync();

    partial void OnSelectedContractChanged(ContractRecordEntity? value)
    {
        if (value is null)
        {
            return;
        }

        EditingContractId = value.Id;
        EditContractNumber = value.ContractNumber;
        EditContractTitle = value.Title;
        EditCounterpartyName = value.CounterpartyName;
        EditContractType = value.ContractType;
        if (DateTime.TryParse(value.SignedDateKey, out DateTime s)) EditSignedDate = s;
        if (DateTime.TryParse(value.StartDateKey, out DateTime st)) EditStartDate = st;
        if (DateTime.TryParse(value.ExpiryDateKey, out DateTime ex)) EditExpiryDate = ex;
        EditValueVndText = value.ValueVnd.ToString("F0");
        EditContractStatus = value.Status;
        EditContractAttachPath = value.FileName ?? string.Empty;
        EditContractNotes = value.Notes;
    }

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

        await RefreshContractsAsync();
    }

    [RelayCommand]
    public async Task RefreshContractsAsync()
    {
        IReadOnlyList<ContractRecordEntity> all = await _contractRegistry.GetContractsAsync();
        TotalContractsCount = all.Count;
        ExpiringSoonContractsCount = all.Count(c => c.Status == "ExpiringSoon");
        ExpiredContractsCount = all.Count(c => c.Status == "Expired");

        IReadOnlyList<ContractRecordEntity> filtered = await _contractRegistry.GetContractsAsync(ContractStatusFilter, ContractSearchQuery);
        Contracts.Clear();
        foreach (ContractRecordEntity c in filtered)
        {
            Contracts.Add(c);
        }
    }

    [RelayCommand]
    private void NewContractForm()
    {
        EditingContractId = Guid.Empty;
        EditContractNumber = $"HĐ-{DateTime.Now:yyyyMMdd}-{Contracts.Count + 1:D2}";
        EditContractTitle = string.Empty;
        EditCounterpartyName = string.Empty;
        EditContractType = "Customer";
        EditSignedDate = DateTime.Today;
        EditStartDate = DateTime.Today;
        EditExpiryDate = DateTime.Today.AddYears(1);
        EditValueVndText = "0";
        EditContractStatus = "Active";
        EditContractAttachPath = string.Empty;
        EditContractNotes = string.Empty;
    }

    [RelayCommand]
    private void BrowseContractAttachment()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Chọn bản scan / Word hợp đồng (tối đa 15 MB)",
            Filter = "Tài liệu (*.pdf;*.docx;*.doc;*.png;*.jpg)|*.pdf;*.docx;*.doc;*.png;*.jpg|Tất cả (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            EditContractAttachPath = dlg.FileName;
        }
    }

    [RelayCommand]
    private async Task SaveContractAsync()
    {
        _ = decimal.TryParse(EditValueVndText.Replace(",", "").Replace(".", "").Trim(), out decimal val);
        string? attachPath = File.Exists(EditContractAttachPath) ? EditContractAttachPath : null;

        try
        {
            ContractRecordEntity saved = await _contractRegistry.SaveContractAsync(new ContractRecordEntity
            {
                Id = EditingContractId,
                ContractNumber = EditContractNumber,
                Title = EditContractTitle,
                CounterpartyName = EditCounterpartyName,
                ContractType = EditContractType,
                SignedDateKey = EditSignedDate.ToString("yyyy-MM-dd"),
                StartDateKey = EditStartDate.ToString("yyyy-MM-dd"),
                ExpiryDateKey = EditExpiryDate.ToString("yyyy-MM-dd"),
                ValueVnd = val,
                Status = EditContractStatus,
                Notes = EditContractNotes
            }, attachPath);

            StatusMessage = $"Đã lưu hợp đồng {saved.ContractNumber} ({saved.Status}).";
            EditingContractId = saved.Id;
            await RefreshContractsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi lưu hợp đồng: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PushContractExpiryToTimelineAsync()
    {
        if (SelectedContract is null)
        {
            StatusMessage = "Chọn một hợp đồng trong sổ theo dõi để đẩy lịch nhắc hạn lên Timeline chung.";
            return;
        }

        try
        {
            TimelinePlanEntity plan = await _contractRegistry.SyncExpiryToTimelineAsync(SelectedContract.Id);
            StatusMessage = $"Đã đồng bộ mốc nhắc hết hạn '{plan.Title}' lên Bảng Timeline chung.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Không thể đẩy lên Timeline: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenContractAttachment()
    {
        if (SelectedContract is null || string.IsNullOrWhiteSpace(SelectedContract.VaultFilePath))
        {
            StatusMessage = "Hợp đồng này chưa có tệp đính kèm trong Vault.";
            return;
        }

        string fullPath = Path.GetFullPath(Path.Combine(
            _paths.VaultDirectory,
            SelectedContract.VaultFilePath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        }
        else
        {
            StatusMessage = "Không tìm thấy tệp hợp đồng trong Vault cục bộ.";
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedContractAsync()
    {
        if (SelectedContract is null)
        {
            return;
        }

        await _contractRegistry.DeleteContractAsync(SelectedContract.Id);
        StatusMessage = $"Đã xóa hợp đồng {SelectedContract.ContractNumber}.";
        await RefreshContractsAsync();
    }

    [RelayCommand]
    private void ExportContractsExcel()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Xuất Sổ Hợp đồng Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"SoHopDong_{DateTime.Now:yyyyMMdd}.xlsx"
        };

        if (dlg.ShowDialog() == true)
        {
            _contractExcel.ExportContracts(dlg.FileName, _session.CompanyName, Contracts.ToArray());
            StatusMessage = $"Đã xuất Sổ Hợp đồng Excel: {dlg.FileName}";
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
