using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.App.Services;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed partial class DbInspectorViewModel : ObservableObject
{
    private readonly ReadOnlyDbInspectorService _inspector;
    private readonly ILocalStoragePaths _paths;

    public DbInspectorViewModel(ReadOnlyDbInspectorService inspector, ILocalStoragePaths paths)
    {
        _inspector = inspector;
        _paths = paths;
    }

    public ObservableCollection<AccountingInvoiceEntity> Invoices { get; } = [];
    public ObservableCollection<LegalContractReviewEntity> ContractReviews { get; } = [];
    public ObservableCollection<OfficeDocumentRecordEntity> OfficeRecords { get; } = [];
    public ObservableCollection<AuditLogEntity> AuditLogs { get; } = [];

    [ObservableProperty] private string _selectedDbPath = string.Empty;
    [ObservableProperty] private string _snapshotHeader = "Chọn tệp .db bản chụp từ máy Kế toán / Pháp chế (mở chế độ Read-Only, không trộn vào DB Giám đốc).";

    [RelayCommand]
    private async Task BrowseAndLoadAsync()
    {
        string snapshotsDir = Path.Combine(_paths.DirectorCacheDirectory, "Snapshots");
        var dlg = new OpenFileDialog
        {
            Title = "Mở bản chụp CSDL (.db) ở chế độ Chỉ đọc",
            Filter = "SQLite Database (*.db)|*.db",
            InitialDirectory = Directory.Exists(snapshotsDir) ? snapshotsDir : _paths.DirectorCacheDirectory
        };

        if (dlg.ShowDialog() == true)
        {
            await LoadFileAsync(dlg.FileName);
        }
    }

    public async Task LoadFileAsync(string dbFilePath)
    {
        SelectedDbPath = dbFilePath;
        InspectedDatabaseSnapshot snap = await _inspector.InspectAsync(dbFilePath);

        Invoices.Clear();
        foreach (AccountingInvoiceEntity i in snap.Invoices) Invoices.Add(i);

        ContractReviews.Clear();
        foreach (LegalContractReviewEntity r in snap.ContractReviews) ContractReviews.Add(r);

        OfficeRecords.Clear();
        foreach (OfficeDocumentRecordEntity o in snap.OfficeRecords) OfficeRecords.Add(o);

        AuditLogs.Clear();
        foreach (AuditLogEntity a in snap.AuditLogs) AuditLogs.Add(a);

        string companyLabel = snap.CompanyProfile is null
            ? "Không rõ công ty"
            : $"{snap.CompanyProfile.CompanyName} (MST: {snap.CompanyProfile.TaxCode}, Schema v{snap.CompanyProfile.AppSchemaVersion})";

        SnapshotHeader = $"[READ-ONLY] {Path.GetFileName(dbFilePath)} • {companyLabel} • {Invoices.Count} hóa đơn XML, {ContractReviews.Count} hồ sơ hợp đồng.";
    }
}
