using EzCompany.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.Modules.Office;

public sealed class OfficeModule : IAppModule
{
    public ModuleMetadata Metadata { get; } = new(
        ModuleId: ModuleIds.OfficePdfStudio,
        DisplayName: "Văn phòng PDF Studio",
        Description: "Gộp, tách, xoay trang và đóng dấu Watermark chéo PDF 100% offline.",
        IconGlyph: "DocumentPdf24",
        Version: "0.1.0",
        Category: "Văn phòng",
        DisplayOrder: 30,
        RequiredPermissions: ["office.pdf.use"]);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<LocalPdfStudioService>();
        services.AddTransient<OfficePdfStudioViewModel>();
        services.AddTransient<OfficePdfStudioView>();
    }

    public object CreateView(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<OfficePdfStudioView>();

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
}
