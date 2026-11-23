using EzCompany.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EzCompany.Core.Crypto;

public static class CryptoServiceCollectionExtensions
{
    public static IServiceCollection AddEzCrypto(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Ed25519Service>();
        services.AddSingleton<AesGcmService>();
        services.AddSingleton<EciesCryptoService>();
        services.AddSingleton<Argon2PasswordHasher>();
        services.AddSingleton<DpapiSecretStore>();
        services.AddSingleton<LocalKeyStore>();
        services.AddSingleton<RecoveryKitService>();
        services.AddSingleton<ClockGuardService>();
        services.AddSingleton<PolicyEngine>();
        services.AddSingleton<IPermissionService, PermissionService>();
        return services;
    }
}
