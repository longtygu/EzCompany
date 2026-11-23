using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.Core.Abstractions;

/// <summary>
/// Extension contract implemented by every business module in Ez Company.
/// Allows community developers to add capabilities without modifying core assemblies.
/// </summary>
public interface IAppModule
{
    ModuleMetadata Metadata { get; }

    void RegisterServices(IServiceCollection services);

    object CreateView(IServiceProvider serviceProvider);

    Task InitializeAsync(CancellationToken ct = default);
}
