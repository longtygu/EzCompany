using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Discovery;
using EzCompany.Core.LanAgent.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EzCompany.Core.LanAgent;

/// <summary>
/// Director background coordinator:
/// 1. Flushes queued <see cref="PairedDeviceEntity.PendingPolicyJson"/> when offline workstations reconnect.
/// 2. Updates peer health metrics from <c>/api/v1/health</c>.
/// 3. Synchronizes <c>PairedDevices</c> registries across two Director nodes of the same company by highest <c>PolicyVersion</c>.
/// </summary>
public sealed class DirectorFleetSyncService : BackgroundService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly LocalKeyStore _keyStore;
    private readonly ILanDiscoveryService _discovery;
    private readonly DirectorLanClient _lanClient;
    private readonly ILocalStoragePaths _paths;
    private readonly ILogger<DirectorFleetSyncService> _logger;

    public DirectorFleetSyncService(
        IDbContextFactory<AppDbContext> appDbFactory,
        LocalKeyStore keyStore,
        ILanDiscoveryService discovery,
        DirectorLanClient lanClient,
        ILocalStoragePaths paths,
        ILogger<DirectorFleetSyncService> logger)
    {
        _appDbFactory = appDbFactory;
        _keyStore = keyStore;
        _discovery = discovery;
        _lanClient = lanClient;
        _paths = paths;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        if (!_keyStore.HasRootPrivateKey)
        {
            return;
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsDirectorNode)
        {
            return;
        }

        string companyIdStr = company.CompanyId.ToString("D");
        Dictionary<string, DiscoveredPeer> livePeers = _discovery.GetPeers()
            .Where(p => string.Equals(p.CompanyId, companyIdStr, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.MachineId, StringComparer.OrdinalIgnoreCase);

        List<PairedDeviceEntity> devices = await db.PairedDevices.ToListAsync(ct).ConfigureAwait(false);

        foreach (PairedDeviceEntity dev in devices)
        {
            if (string.Equals(dev.MachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
            {
                dev.IsOnline = true;
                dev.LastSeenUtc = DateTimeOffset.UtcNow;
                continue;
            }

            if (!livePeers.TryGetValue(dev.MachineId, out DiscoveredPeer? peer))
            {
                dev.IsOnline = false;
                continue;
            }

            dev.LastKnownIp = peer.IpAddress;
            dev.LastKnownPort = peer.Port;
            dev.IsOnline = true;
            dev.LastSeenUtc = DateTimeOffset.UtcNow;

            string endpoint = peer.Endpoint;
            if (!string.IsNullOrWhiteSpace(dev.PendingPolicyJson))
            {
                try
                {
                    ApplyPolicyResponseDto applied = await _lanClient
                        .PushPolicyAsync(endpoint, dev.PendingPolicyJson, ct)
                        .ConfigureAwait(false);
                    if (applied.Applied)
                    {
                        dev.PendingPolicyJson = null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Pending policy push deferred for {MachineId}.", dev.MachineId);
                }
            }

            try
            {
                WorkstationHealthDto health = await _lanClient.GetHealthAsync(endpoint, ct).ConfigureAwait(false);
                dev.AppVersion = health.AppVersion;
                dev.SchemaVersion = health.SchemaVersion;
                dev.DbSizeBytes = health.DbSizeBytes;
                dev.VaultSizeBytes = health.VaultSizeBytes;
                dev.FreeDiskPercent = health.FreeDiskPercent;
            }
            catch
            {
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // 2-Director synchronization
        foreach (DiscoveredPeer peerDirector in livePeers.Values.Where(p => p.IsDirector))
        {
            try
            {
                IReadOnlyList<RegistryDeviceSyncDto> remoteRegistry = await _lanClient
                    .GetDirectorRegistryAsync(peerDirector.Endpoint, ct)
                    .ConfigureAwait(false);
                await LanAgentHost.MergeDirectorRegistryAsync(_appDbFactory, remoteRegistry, ct).ConfigureAwait(false);

                List<RegistryDeviceSyncDto> localSnapshot = await db.PairedDevices.AsNoTracking()
                    .Select(d => new RegistryDeviceSyncDto(
                        d.CompanyId, d.MachineId, d.DevicePublicKeyBase64, d.UserId, d.Username, d.FullName,
                        d.Role, d.CurrentPolicyVersion, d.CurrentPolicyJson, d.PendingPolicyJson,
                        d.LastKnownIp, d.LastKnownPort, d.AppVersion, d.SchemaVersion, d.PairedAtUtc, d.PairedByAdminName))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                await _lanClient.PushDirectorRegistryAsync(peerDirector.Endpoint, localSnapshot, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "2-Director sync deferred with {Peer}.", peerDirector.MachineId);
            }
        }
    }
}
