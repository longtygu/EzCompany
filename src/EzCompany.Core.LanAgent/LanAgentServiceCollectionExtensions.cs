using EzCompany.Core.LanAgent.Database;
using EzCompany.Core.LanAgent.Discovery;
using EzCompany.Core.LanAgent.Pairing;
using EzCompany.Core.LanAgent.Recovery;
using EzCompany.Core.LanAgent.Security;
using EzCompany.Core.LanAgent.Tasks;
using EzCompany.Core.LanAgent.Vault;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.Core.LanAgent;

public static class LanAgentServiceCollectionExtensions
{
    public static IServiceCollection AddEzLanAgent(this IServiceCollection services, bool bindLoopbackOnly = false)
    {
        services.AddSingleton(new LanAgentOptions(BindLoopbackOnly: bindLoopbackOnly, PreferredPort: 0));
        services.AddHttpClient("EzLan", client => client.Timeout = TimeSpan.FromSeconds(10));

        services.AddSingleton<DirectorRequestSigner>();
        services.AddSingleton<DeviceRequestSigner>();
        services.AddSingleton<NonceReplayCache>();
        services.AddSingleton<VaultPathGuard>();
        services.AddSingleton<DatabaseSnapshotService>();

        services.AddSingleton<LanDiscoveryService>();
        services.AddSingleton<ILanDiscoveryService>(sp => sp.GetRequiredService<LanDiscoveryService>());
        services.AddHostedService(sp => sp.GetRequiredService<LanDiscoveryService>());

        services.AddSingleton<PairingSessionManager>();
        services.AddSingleton<LanPairingClient>();
        services.AddSingleton<AccountRecoveryManager>();
        services.AddSingleton<LanAccountRecoveryClient>();
        services.AddSingleton<LanTaskClient>();
        services.AddSingleton<PeerTaskSyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<PeerTaskSyncService>());
        services.AddSingleton<DirectorLanClient>();

        services.AddSingleton<LanAgentHost>();
        services.AddHostedService(sp => sp.GetRequiredService<LanAgentHost>());

        services.AddSingleton<DirectorFleetSyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<DirectorFleetSyncService>());

        return services;
    }
}
