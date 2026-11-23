using EzCompany.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.Modules.Accounting;

public sealed class AccountingModule : IAppModule
{
    public ModuleMetadata Metadata { get; } = new(
        ModuleId: ModuleIds.AccountingInvoiceXml,
        DisplayName: "Kế toán Hóa đơn XML",
        Description: "Đọc, kiểm tra trùng/sai lệch cộng dồn hóa đơn XML NĐ 123/2020 & NĐ 70/2025 và xuất Excel.",
        IconGlyph: "Receipt24",
        Version: "0.1.0",
        Category: "Kế toán",
        DisplayOrder: 10,
        RequiredPermissions: ["accounting.invoice.read"]);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<VietnameseInvoiceXmlParser>();
        services.AddSingleton<AccountingInvoiceService>();
        services.AddSingleton<AccountingExcelExporter>();
        services.AddTransient<AccountingInvoiceViewModel>();
        services.AddTransient<AccountingInvoiceView>();
    }

    public object CreateView(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<AccountingInvoiceView>();

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
}
