using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Collaboration;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed partial class SharedDocumentsViewModel : ObservableObject
{
    private readonly SharedDocumentService _docsService;
    private readonly LanCollaborationClient _collabClient;
    private readonly ILocalStoragePaths _paths;
    private readonly IEventBus _eventBus;

    public SharedDocumentsViewModel(
        SharedDocumentService docsService,
        LanCollaborationClient collabClient,
        ILocalStoragePaths paths,
        IEventBus eventBus)
    {
        _docsService = docsService;
        _collabClient = collabClient;
        _paths = paths;
        _eventBus = eventBus;

        _eventBus.Subscribe<SharedDocumentUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? RefreshRequested;

    public ObservableCollection<SharedDocumentEntity> Documents { get; } = [];
    public IReadOnlyList<string> CategoryFilters { get; } = ["ALL", .. SharedDocumentCategories.All];
    public IReadOnlyList<string> UploadCategories { get; } = SharedDocumentCategories.All;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _categoryFilter = "ALL";
    [ObservableProperty] private SharedDocumentEntity? _selectedDocument;

    [ObservableProperty] private string _newDocTitle = string.Empty;
    [ObservableProperty] private string _newDocCategory = SharedDocumentCategories.Policy;
    [ObservableProperty] private string _newDocDescription = string.Empty;
    [ObservableProperty] private string _newDocFilePath = string.Empty;

    [ObservableProperty] private string _statusBanner = "Kho tài liệu dùng chung nội bộ công ty (đồng bộ SHA-256 qua LAN P2P).";

    partial void OnSearchQueryChanged(string value) => _ = RefreshAllAsync();
    partial void OnCategoryFilterChanged(string value) => _ = RefreshAllAsync();

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IReadOnlyList<SharedDocumentEntity> list = await _docsService.GetActiveDocumentsAsync(CategoryFilter, SearchQuery);
        Documents.Clear();
        foreach (SharedDocumentEntity d in list)
        {
            Documents.Add(d);
        }
    }

    [RelayCommand]
    private async Task SyncFromLanAsync()
    {
        int changes = await _collabClient.SyncAllEnterpriseEntitiesWithOnlinePeersAsync();
        await RefreshAllAsync();
        StatusBanner = $"Đã đồng bộ danh mục tài liệu dùng chung qua LAN ({changes} cập nhật).";
    }

    [RelayCommand]
    private void BrowseUploadFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Chọn tài liệu tải lên Kho dùng chung (tối đa 15 MB)",
            Filter = "Tất cả tệp (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            NewDocFilePath = dlg.FileName;
            if (string.IsNullOrWhiteSpace(NewDocTitle))
            {
                NewDocTitle = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }
    }

    [RelayCommand]
    private async Task UploadDocumentAsync()
    {
        try
        {
            SharedDocumentEntity doc = await _docsService.UploadDocumentAsync(
                NewDocTitle,
                NewDocCategory,
                NewDocDescription,
                NewDocFilePath);

            NewDocTitle = string.Empty;
            NewDocDescription = string.Empty;
            NewDocFilePath = string.Empty;
            _ = await _collabClient.SyncAllEnterpriseEntitiesWithOnlinePeersAsync();
            StatusBanner = $"Đã tải lên tài liệu '{doc.Title}' và phát đồng bộ LAN.";
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Lỗi tải lên: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenOrDownloadDocumentAsync()
    {
        if (SelectedDocument is null)
        {
            StatusBanner = "Chọn một tài liệu trong danh sách.";
            return;
        }

        string? fullPath = null;
        if (!string.IsNullOrWhiteSpace(SelectedDocument.VaultRelativePath))
        {
            string candidate = Path.GetFullPath(Path.Combine(
                _paths.VaultDirectory,
                SelectedDocument.VaultRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(candidate))
            {
                fullPath = candidate;
            }
        }

        if (fullPath is null)
        {
            StatusBanner = $"Đang tải '{SelectedDocument.FileName}' từ máy đồng nghiệp trong LAN...";
            fullPath = await _collabClient.DownloadSharedDocumentFromPeersAsync(SelectedDocument.Id);
            await RefreshAllAsync();
        }

        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
        {
            StatusBanner = "Chưa thể tải tệp từ LAN (máy giữ tệp gốc đang Offline).";
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
        {
            UseShellExecute = true
        });
        StatusBanner = $"Đã mở vị trí tài liệu tại Vault cục bộ: {Path.GetFileName(fullPath)}";
    }

    [RelayCommand]
    private async Task DeleteSelectedDocumentAsync()
    {
        if (SelectedDocument is null)
        {
            return;
        }

        await _docsService.SoftDeleteDocumentAsync(SelectedDocument.Id);
        _ = await _collabClient.SyncAllEnterpriseEntitiesWithOnlinePeersAsync();
        StatusBanner = $"Đã xóa tài liệu '{SelectedDocument.Title}'.";
        await RefreshAllAsync();
    }
}
