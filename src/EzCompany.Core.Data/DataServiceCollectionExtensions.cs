using EzCompany.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.Core.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddEzData(this IServiceCollection services, ILocalStoragePaths paths)
    {
        services.AddSingleton<SqliteWalConnectionInterceptor>();

        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            SqliteWalConnectionInterceptor interceptor = sp.GetRequiredService<SqliteWalConnectionInterceptor>();
            options.UseSqlite($"Data Source={paths.AppDbPath};Cache=Shared")
                .AddInterceptors(interceptor);
        });

        services.AddDbContextFactory<TelemetryDbContext>((sp, options) =>
        {
            SqliteWalConnectionInterceptor interceptor = sp.GetRequiredService<SqliteWalConnectionInterceptor>();
            options.UseSqlite($"Data Source={paths.TelemetryDbPath};Cache=Shared")
                .AddInterceptors(interceptor);
        });

        services.AddSingleton<IAuditLogger, DatabaseAuditLogger>();
        services.AddSingleton<GenesisBootstrapService>();
        services.AddSingleton<WorkTaskService>();
        services.AddSingleton<CompanyBrandingService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<TimelinePlanService>();
        services.AddSingleton<HrAttendanceService>();
        return services;
    }
}
