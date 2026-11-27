using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.Win32;

namespace EzCompany.Modules.Accounting;

public sealed partial class AccountingInvoiceViewModel : ObservableObject
{
    private readonly AccountingInvoiceService _invoiceService;
    private readonly AccountingExcelExporter _exporter;
    private readonly AccountingLedgerService _ledgerService;
    private readonly AccountingLedgerExcelExporter _ledgerExporter;
    private readonly ISessionContext _session;
    private readonly IEventBus _eventBus;
    private List<AccountingInvoiceEntity> _allInvoices = [];

    public AccountingInvoiceViewModel(
        AccountingInvoiceService invoiceService,
        AccountingExcelExporter exporter,
        AccountingLedgerService ledgerService,
        AccountingLedgerExcelExporter ledgerExporter,
        ISessionContext session,
        IEventBus eventBus)
    {
        _invoiceService = invoiceService;
        _exporter = exporter;
        _ledgerService = ledgerService;
        _ledgerExporter = ledgerExporter;
        _session = session;
        _eventBus = eventBus;

        _eventBus.Subscribe<ExpenseRequestUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? RefreshRequested;

    public ObservableCollection<AccountingInvoiceEntity> Invoices { get; } = [];
    public ObservableCollection<CashbookEntryEntity> CashbookEntries { get; } = [];
    public ObservableCollection<ExpenseRequestEntity> ExpenseRequests { get; } = [];

    public IReadOnlyList<string> CashbookTypes { get; } = ["Income", "Expense"];
    public IReadOnlyList<string> CashbookTypeFilters { get; } = ["ALL", "Income", "Expense"];
    public IReadOnlyList<string> PaymentMethods { get; } = ["Cash", "BankTransfer"];
    public IReadOnlyList<string> ExpenseRequestTypeOptions { get; } = ExpenseRequestTypes.All;

    [ObservableProperty] private string _statusMessage = "Chọn một hoặc nhiều tệp hóa đơn điện tử .xml (NĐ 123/2020 & NĐ 70/2025) để kiểm tra.";
    [ObservableProperty] private string _summaryText = "0 hóa đơn";

    // Invoice search & filter
    [ObservableProperty] private AccountingInvoiceEntity? _selectedInvoice;
    [ObservableProperty] private string _invoiceSearchQuery = string.Empty;
    [ObservableProperty] private int _invoiceFilterMonth;
    [ObservableProperty] private int _invoiceFilterYear;
    [ObservableProperty] private bool _invoiceAlertsOnly;

    // Cashbook tab
    [ObservableProperty] private string _cashbookSummaryIncome = "0 đ";
    [ObservableProperty] private string _cashbookSummaryExpense = "0 đ";
    [ObservableProperty] private string _cashbookSummaryBalance = "0 đ";
    [ObservableProperty] private string _cashbookTypeFilter = "ALL";
    [ObservableProperty] private CashbookEntryEntity? _selectedCashbookEntry;
    [ObservableProperty] private DateTime _newEntryDate = DateTime.Today;
    [ObservableProperty] private string _newEntryType = "Expense";
    [ObservableProperty] private string _newEntryCategory = "Văn phòng phẩm";
    [ObservableProperty] private string _newEntryAmountText = string.Empty;
    [ObservableProperty] private string _newEntryMethod = "BankTransfer";
    [ObservableProperty] private string _newEntryRefCode = string.Empty;
    [ObservableProperty] private string _newEntryDescription = string.Empty;

    // Expense request tab
    [ObservableProperty] private ExpenseRequestEntity? _selectedExpenseRequest;
    [ObservableProperty] private string _newExpenseType = ExpenseRequestTypes.Advance;
    [ObservableProperty] private string _newExpenseCategory = "Công tác / Tiếp khách";
    [ObservableProperty] private string _newExpenseAmountText = string.Empty;
    [ObservableProperty] private string _newExpenseDescription = string.Empty;
    [ObservableProperty] private string _newExpenseReceiptPath = string.Empty;
    [ObservableProperty] private string _disbursePaymentMethod = "BankTransfer";

    partial void OnInvoiceSearchQueryChanged(string value) => ApplyInvoiceFilter();
    partial void OnInvoiceFilterMonthChanged(int value) => ApplyInvoiceFilter();
    partial void OnInvoiceFilterYearChanged(int value) => ApplyInvoiceFilter();
    partial void OnInvoiceAlertsOnlyChanged(bool value) => ApplyInvoiceFilter();
    partial void OnCashbookTypeFilterChanged(string value) => _ = LoadCashbookAndExpensesAsync();

    [RelayCommand]
    public async Task LoadSavedAsync()
    {
        _allInvoices = (await _invoiceService.GetAllInvoicesAsync()).ToList();
        ApplyInvoiceFilter();
        await LoadCashbookAndExpensesAsync();
    }

    private async Task LoadCashbookAndExpensesAsync()
    {
        IReadOnlyList<CashbookEntryEntity> ledger = await _ledgerService.GetCashbookEntriesAsync(null, null, CashbookTypeFilter);
        CashbookEntries.Clear();
        foreach (CashbookEntryEntity item in ledger)
        {
            CashbookEntries.Add(item);
        }

        IReadOnlyList<CashbookEntryEntity> allEntries = await _ledgerService.GetCashbookEntriesAsync();
        (decimal inc, decimal exp, decimal bal) = AccountingLedgerService.Summarize(allEntries);
        CashbookSummaryIncome = $"{inc:N0} đ";
        CashbookSummaryExpense = $"{exp:N0} đ";
        CashbookSummaryBalance = $"{bal:N0} đ";

        IReadOnlyList<ExpenseRequestEntity> exps = await _ledgerService.GetExpenseRequestsAsync();
        ExpenseRequests.Clear();
        foreach (ExpenseRequestEntity r in exps)
        {
            ExpenseRequests.Add(r);
        }
    }

    private void ApplyInvoiceFilter()
    {
        IEnumerable<AccountingInvoiceEntity> q = _allInvoices;
        if (!string.IsNullOrWhiteSpace(InvoiceSearchQuery))
        {
            string s = InvoiceSearchQuery.Trim();
            q = q.Where(x =>
                x.SellerName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                x.SellerTaxCode.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                x.InvoiceNumber.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                x.InvoiceSeries.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        if (InvoiceFilterMonth is >= 1 and <= 12)
        {
            q = q.Where(x => x.IssueDate.ToLocalTime().Month == InvoiceFilterMonth);
        }

        if (InvoiceFilterYear >= 2000)
        {
            q = q.Where(x => x.IssueDate.ToLocalTime().Year == InvoiceFilterYear);
        }

        if (InvoiceAlertsOnly)
        {
            q = q.Where(x => x.IsDuplicate || x.HasMathMismatch);
        }

        Invoices.Clear();
        foreach (AccountingInvoiceEntity item in q)
        {
            Invoices.Add(item);
        }

        UpdateSummary();
    }

    [RelayCommand]
    private void ClearInvoiceFilters()
    {
        InvoiceSearchQuery = string.Empty;
        InvoiceFilterMonth = 0;
        InvoiceFilterYear = 0;
        InvoiceAlertsOnly = false;
    }

    [RelayCommand]
    private async Task DeleteSelectedInvoiceAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        await _ledgerService.DeleteInvoiceAsync(SelectedInvoice.Id);
        StatusMessage = $"Đã xóa hóa đơn {SelectedInvoice.InvoiceSeries}-{SelectedInvoice.InvoiceNumber}.";
        await LoadSavedAsync();
    }

    [RelayCommand]
    private async Task AddCashbookEntryAsync()
    {
        if (!decimal.TryParse(NewEntryAmountText.Replace(",", "").Replace(".", "").Trim(), out decimal amount) || amount <= 0)
        {
            StatusMessage = "Vui lòng nhập số tiền hợp lệ (> 0).";
            return;
        }

        try
        {
            await _ledgerService.SaveCashbookEntryAsync(new CashbookEntryEntity
            {
                EntryDateKey = NewEntryDate.ToString("yyyy-MM-dd"),
                EntryType = NewEntryType,
                Category = NewEntryCategory,
                AmountVnd = amount,
                PaymentMethod = NewEntryMethod,
                ReferenceCode = NewEntryRefCode,
                Description = NewEntryDescription
            });

            NewEntryAmountText = string.Empty;
            NewEntryRefCode = string.Empty;
            NewEntryDescription = string.Empty;
            StatusMessage = "Đã ghi nhận giao dịch vào Sổ Thu - Chi.";
            await LoadCashbookAndExpensesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi lưu sổ quỹ: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedCashbookEntryAsync()
    {
        if (SelectedCashbookEntry is null)
        {
            return;
        }

        await _ledgerService.DeleteCashbookEntryAsync(SelectedCashbookEntry.Id);
        StatusMessage = "Đã xóa bút toán Sổ Quỹ.";
        await LoadCashbookAndExpensesAsync();
    }

    [RelayCommand]
    private void ExportCashbookExcel()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Xuất Sổ Quỹ Thu Chi Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"SoQuyThuChi_{DateTime.Now:yyyyMMdd}.xlsx"
        };

        if (dlg.ShowDialog() == true)
        {
            _ledgerExporter.ExportCashbook(dlg.FileName, _session.CompanyName, CashbookEntries.ToArray());
            StatusMessage = $"Đã xuất Sổ Quỹ Thu Chi Excel: {dlg.FileName}";
        }
    }

    [RelayCommand]
    private void BrowseExpenseReceipt()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Chọn chứng từ / ảnh hóa đơn (tối đa 10 MB)",
            Filter = "Hình ảnh & PDF (*.png;*.jpg;*.jpeg;*.pdf)|*.png;*.jpg;*.jpeg;*.pdf|Tất cả (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            NewExpenseReceiptPath = dlg.FileName;
        }
    }

    [RelayCommand]
    private async Task SubmitExpenseRequestAsync()
    {
        if (!decimal.TryParse(NewExpenseAmountText.Replace(",", "").Replace(".", "").Trim(), out decimal amount) || amount <= 0)
        {
            StatusMessage = "Nhập số tiền đề xuất hợp lệ (> 0).";
            return;
        }

        try
        {
            await _ledgerService.SubmitExpenseRequestAsync(
                NewExpenseType,
                NewExpenseCategory,
                amount,
                NewExpenseDescription,
                string.IsNullOrWhiteSpace(NewExpenseReceiptPath) ? null : NewExpenseReceiptPath);

            NewExpenseAmountText = string.Empty;
            NewExpenseDescription = string.Empty;
            NewExpenseReceiptPath = string.Empty;
            StatusMessage = "Đã tạo đề xuất Tạm ứng / Thanh toán và gửi đồng bộ LAN.";
            await LoadCashbookAndExpensesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Không thể gửi đề xuất: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisburseSelectedExpenseAsync()
    {
        if (SelectedExpenseRequest is null)
        {
            StatusMessage = "Chọn một đề xuất đã được Giám đốc phê duyệt để giải ngân.";
            return;
        }

        try
        {
            await _ledgerService.DisburseExpenseRequestAsync(SelectedExpenseRequest.Id, DisbursePaymentMethod);
            StatusMessage = $"Đã chi tiền cho đề xuất của {SelectedExpenseRequest.RequesterFullName} và tự động hạch toán Sổ Chi.";
            await LoadCashbookAndExpensesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
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
        SummaryText = $"Đang hiển thị: {Invoices.Count}/{_allInvoices.Count} HĐ • Cảnh báo: {alerts} • Tổng tiền: {total:N0} đ ({VietnameseNumberToWordsConverter.Convert(total)})";
    }
}
