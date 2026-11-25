using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Discovery;
using EzCompany.Core.LanAgent.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EzCompany.Core.LanAgent.Tasks;

/// <summary>
/// Periodically dispatches queued peer-to-peer task assignments, handovers, and status updates
/// whenever target workstations are reachable on LAN (or via explicit endpoint overrides in tests).
/// </summary>
public sealed class PeerTaskSyncService : BackgroundService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILanDiscoveryService _discovery;
    private readonly LanTaskClient _taskClient;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly ILogger<PeerTaskSyncService> _logger;

    public PeerTaskSyncService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILanDiscoveryService discovery,
        LanTaskClient taskClient,
        ILocalStoragePaths paths,
        ISessionContext session,
        ILogger<PeerTaskSyncService> logger)
    {
        _appDbFactory = appDbFactory;
        _discovery = discovery;
        _taskClient = taskClient;
        _paths = paths;
        _session = session;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (_session.IsAuthenticated)
                {
                    await FlushPendingTasksOnceAsync(null, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task<int> FlushPendingTasksOnceAsync(
        IReadOnlyDictionary<string, string>? endpointOverrides = null,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsGenesisCompleted)
        {
            return 0;
        }

        List<WorkTaskEntity> pending = await db.WorkTasks
            .Where(t => t.PendingSyncTargetMachineId != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return 0;
        }

        string companyIdStr = company.CompanyId.ToString("D");
        Dictionary<string, string> discoveredEndpoints = _discovery.GetPeers()
            .Where(p => string.Equals(p.CompanyId, companyIdStr, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.MachineId, p => p.Endpoint, StringComparer.OrdinalIgnoreCase);

        if (company.IsDirectorNode)
        {
            List<PairedDeviceEntity> paired = await db.PairedDevices.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
            foreach (PairedDeviceEntity dev in paired)
            {
                if (!discoveredEndpoints.ContainsKey(dev.MachineId) &&
                    dev.IsOnline &&
                    !string.IsNullOrWhiteSpace(dev.LastKnownIp) &&
                    dev.LastKnownPort > 0)
                {
                    discoveredEndpoints[dev.MachineId] = $"{dev.LastKnownIp}:{dev.LastKnownPort}";
                }
            }
        }

        int syncedCount = 0;
        foreach (WorkTaskEntity task in pending)
        {
            string targetId = task.PendingSyncTargetMachineId!;
            string? targetEndpoint = null;

            if (endpointOverrides is not null && endpointOverrides.TryGetValue(targetId, out string? ov))
            {
                targetEndpoint = ov;
            }
            else if (discoveredEndpoints.TryGetValue(targetId, out string? found))
            {
                targetEndpoint = found;
            }

            if (string.IsNullOrWhiteSpace(targetEndpoint))
            {
                continue;
            }

            try
            {
                string action = string.IsNullOrWhiteSpace(task.PendingSyncAction) ? "Sync" : task.PendingSyncAction;
                string note = action switch
                {
                    "Assign" => $"Đã chuyển công việc từ {task.AssignerFullName} sang {task.AssigneeFullName}.",
                    "Handover" => string.IsNullOrWhiteSpace(task.HandoverNotes) ? "Đã gửi kết quả bàn giao." : $"Bàn giao: {task.HandoverNotes}",
                    _ => $"Cập nhật trạng thái ({task.Status}): {task.ReviewNotes}".TrimEnd(':', ' ')
                };

                TaskSyncAckDto ack = await _taskClient.SendTaskSyncAsync(targetEndpoint, task, action, note, ct)
                    .ConfigureAwait(false);

                if (ack.Accepted)
                {
                    task.PendingSyncTargetMachineId = null;
                    task.PendingSyncAction = null;
                    syncedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Deferred task sync for {TaskId} -> {TargetMachineId}.", task.Id, targetId);
            }
        }

        if (syncedCount > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return syncedCount;
    }
}
