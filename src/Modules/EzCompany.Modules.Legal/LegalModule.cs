using EzCompany.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.Modules.Legal;

public sealed class LegalModule : IAppModule
{
    public ModuleMetadata Metadata { get; } = new(
        ModuleId: ModuleIds.LegalContractDiff,
        DisplayName: "Pháp chế So sánh Hợp đồng",
        Description: "So sánh 2 phiên bản hợp đồng .docx offline, lưu SHA-256 vào Vault và xuất báo cáo thẩm định.",
        IconGlyph: "DocumentEdit24",
        Version: "0.1.0",
        Category: "Pháp chế",
        DisplayOrder: 20,
        RequiredPermissions: ["legal.contract.read"]);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<DocxTextExtractor>();
        services.AddSingleton<ContractDiffService>();
        services.AddSingleton<ContractReportExporter>();
        services.AddSingleton<ContractRegistryExcelExporter>();
        services.AddTransient<LegalContractDiffViewModel>();
        services.AddTransient<LegalContractDiffView>();
    }

    public object CreateView(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<LegalContractDiffView>();

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
}
