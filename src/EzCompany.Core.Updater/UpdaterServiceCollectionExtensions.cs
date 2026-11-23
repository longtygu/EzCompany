using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.Core.Updater;

public static class UpdaterServiceCollectionExtensions
{
    public static IServiceCollection AddEzUpdater(this IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(UpdaterOptions.SectionName);
        services.Configure<UpdaterOptions>(opts =>
        {
            string? repoUrl = section[nameof(UpdaterOptions.GitHubRepositoryUrl)];
            if (!string.IsNullOrWhiteSpace(repoUrl))
            {
                opts.GitHubRepositoryUrl = repoUrl;
            }

            string? channel = section[nameof(UpdaterOptions.DefaultChannel)];
            if (!string.IsNullOrWhiteSpace(channel))
            {
                opts.DefaultChannel = channel;
            }
        });

        services.AddSingleton<UpdatePolicyGate>();
        services.AddSingleton<VelopackUpdateService>();
        services.AddSingleton<SafeMigrationService>();
        return services;
    }
}
