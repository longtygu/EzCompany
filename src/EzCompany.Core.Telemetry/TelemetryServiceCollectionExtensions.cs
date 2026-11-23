using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.Core.Telemetry;

public static class TelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddEzTelemetry(this IServiceCollection services)
    {
        services.AddSingleton<IWorkspaceWindowAccessor, WorkspaceWindowAccessor>();

        services.AddSingleton<TelemetryCollectorService>();
        services.AddHostedService(sp => sp.GetRequiredService<TelemetryCollectorService>());

        services.AddSingleton<WindowSnapshotService>();
        services.AddHostedService(sp => sp.GetRequiredService<WindowSnapshotService>());

        services.AddSingleton<TelemetryRetentionService>();
        services.AddHostedService(sp => sp.GetRequiredService<TelemetryRetentionService>());

        services.AddSingleton<DirectorTelemetryPullService>();
        services.AddHostedService(sp => sp.GetRequiredService<DirectorTelemetryPullService>());

        return services;
    }
}
