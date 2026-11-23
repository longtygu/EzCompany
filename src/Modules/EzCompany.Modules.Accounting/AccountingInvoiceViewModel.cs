using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Data.Entities;
using Microsoft.Win32;

namespace EzCompany.Modules.Accounting;

public sealed partial class AccountingInvoiceViewModel : ObservableObject
{
    private readonly AccountingInvoiceService _invoiceService;
    private readonly AccountingExcelExporter _exporter;

    public AccountingInvoiceViewModel(AccountingInvoiceService invoiceService, AccountingExcelExporter exporter)
    {
        _invoiceService = invoiceService;
        _exporter = exporter;
    }

    public ObservableCollection<AccountingInvoiceEntity> Invoices { get; } = [];

    [ObservableProperty] private string _statusMessage = "Chọn một hoặc nhiều tệp hóa đơn điện tử .xml (NĐ 123/2020 & NĐ 70/2025) để kiểm tra.";
    [ObservableProperty] private string _summaryText = "0 hóa đơn";

    [RelayCommand]
    public async Task LoadSavedAsync()
    {
        IReadOnlyList<AccountingInvoiceEntity> list = await _invoiceService.GetAllInvoicesAsync();
        Invoices.Clear();
        foreach (AccountingInvoiceEntity item in list)
        {
            Invoices.Add(item);
        }

        UpdateSummary();
    }

    [RelayCommand]
    private async Task ImportXmlFilesAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Chọn các tệp XML hóa đơn điện tử",
            Filter = "Hóa đơn XML (*.xml)|*.xml",
            Multiselect = true
        };

        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0)
        {
            return;
        }

        IReadOnlyList<AccountingInvoiceEntity> imported = await _invoiceService.ImportBatchAsync(dlg.FileNames);
        StatusMessage = $"Đã nhập {imported.Count} hóa đơn (Trùng: {imported.Count(x => x.IsDuplicate)}, Sai lệch >10đ: {imported.Count(x => x.HasMathMismatch)}).";
        await LoadSavedAsync();
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (Invoices.Count == 0)
        {
            StatusMessage = "Chưa có hóa đơn nào để xuất Excel.";
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Xuất Bảng kê Hóa đơn Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"BangKeHoaDon_{DateTime.Now:yyyyMMdd}.xlsx"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        await _exporter.ExportInvoicesToExcelAsync(Invoices.ToList(), dlg.FileName);
        StatusMessage = $"Đã xuất bảng kê Excel kèm tiền bằng chữ: {dlg.FileName}";
    }

    private void UpdateSummary()
    {
        decimal total = Invoices.Sum(x => x.TotalAmount);
        int alerts = Invoices.Count(x => x.IsDuplicate || x.HasMathMismatch);
        SummaryText = $"Tổng số: {Invoices.Count} HĐ • Cảnh báo: {alerts} • Tổng tiền: {total:N0} đ ({VietnameseNumberToWordsConverter.Convert(total)})";
    }
}
