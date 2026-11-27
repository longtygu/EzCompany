using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Collaboration;
using Microsoft.Win32;

namespace EzCompany.App.ViewModels;

public sealed partial class EmployeeRequestsViewModel : ObservableObject
{
    private readonly LeaveRequestService _leaveService;
    private readonly AccountingLedgerService _ledgerService;
    private readonly LanCollaborationClient _collabClient;
    private readonly IEventBus _eventBus;

    public EmployeeRequestsViewModel(
        LeaveRequestService leaveService,
        AccountingLedgerService ledgerService,
        LanCollaborationClient collabClient,
        IEventBus eventBus)
    {
        _leaveService = leaveService;
        _ledgerService = ledgerService;
        _collabClient = collabClient;
        _eventBus = eventBus;

        _eventBus.Subscribe<LeaveRequestUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
        _eventBus.Subscribe<ExpenseRequestUpdatedEvent>(_ => RefreshRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? RefreshRequested;

    public ObservableCollection<LeaveRequestEntity> MyLeaveRequests { get; } = [];
    public ObservableCollection<ExpenseRequestEntity> MyExpenseRequests { get; } = [];

    public IReadOnlyList<string> LeaveTypes { get; } = LeaveRequestTypes.All;
    public IReadOnlyList<string> ExpenseTypes { get; } = ExpenseRequestTypes.All;

    // Tab 1: Leave / Late / Overtime
    [ObservableProperty] private string _leaveType = LeaveRequestTypes.Leave;
    [ObservableProperty] private DateTime _leaveStartDate = DateTime.Today;
    [ObservableProperty] private DateTime _leaveEndDate = DateTime.Today;
    [ObservableProperty] private double _leaveOvertimeHours = 2.0;
    [ObservableProperty] private string _leaveReason = string.Empty;

    // Tab 2: Advance / Reimbursement / Payment
    [ObservableProperty] private string _expenseType = ExpenseRequestTypes.Advance;
    [ObservableProperty] private string _expenseCategory = "Công tác / Mua sắm";
    [ObservableProperty] private string _expenseAmountText = string.Empty;
    [ObservableProperty] private string _expenseDescription = string.Empty;
    [ObservableProperty] private string _expenseReceiptPath = string.Empty;

    [ObservableProperty] private string _statusBanner = "Gửi đơn xin nghỉ phép, tăng ca hoặc đề xuất tạm ứng qua mạng LAN.";

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IReadOnlyList<LeaveRequestEntity> leaves = await _leaveService.GetRequestsAsync(mineOnly: true);
        MyLeaveRequests.Clear();
        foreach (LeaveRequestEntity l in leaves)
        {
            MyLeaveRequests.Add(l);
        }

        IReadOnlyList<ExpenseRequestEntity> exps = await _ledgerService.GetExpenseRequestsAsync(mineOnly: true);
        MyExpenseRequests.Clear();
        foreach (ExpenseRequestEntity e in exps)
        {
            MyExpenseRequests.Add(e);
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        int changes = await _collabClient.SyncAllEnterpriseEntitiesWithOnlinePeersAsync();
        await RefreshAllAsync();
        StatusBanner = $"Đã đồng bộ đơn từ với mạng LAN ({changes} cập nhật).";
    }

    [RelayCommand]
    private async Task SubmitLeaveRequestAsync()
    {
        try
        {
            await _leaveService.SubmitRequestAsync(
                LeaveType,
                DateOnly.FromDateTime(LeaveStartDate),
                DateOnly.FromDateTime(LeaveEndDate),
                LeaveType == LeaveRequestTypes.Overtime ? LeaveOvertimeHours : 0.0,
                LeaveReason);

            LeaveReason = string.Empty;
            _ = await _collabClient.SyncAllEnterpriseEntitiesWithOnlinePeersAsync();
            StatusBanner = "Đã gửi đơn xin phép / tăng ca thành công qua LAN.";
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Không thể gửi đơn: {ex.Message}";
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
            ExpenseReceiptPath = dlg.FileName;
        }
    }

    [RelayCommand]
    private async Task SubmitExpenseRequestAsync()
    {
        if (!decimal.TryParse(ExpenseAmountText.Replace(",", "").Replace(".", "").Trim(), out decimal amount) || amount <= 0)
        {
            StatusBanner = "Vui lòng nhập số tiền đề xuất hợp lệ (> 0).";
            return;
        }

        try
        {
            await _ledgerService.SubmitExpenseRequestAsync(
                ExpenseType,
                ExpenseCategory,
                amount,
                ExpenseDescription,
                string.IsNullOrWhiteSpace(ExpenseReceiptPath) ? null : ExpenseReceiptPath);

            ExpenseAmountText = string.Empty;
            ExpenseDescription = string.Empty;
            ExpenseReceiptPath = string.Empty;
            _ = await _collabClient.SyncAllEnterpriseEntitiesWithOnlinePeersAsync();
            StatusBanner = "Đã gửi đề xuất tạm ứng / thanh toán tới Giám đốc & Kế toán.";
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Không thể gửi đề xuất: {ex.Message}";
        }
    }
}
