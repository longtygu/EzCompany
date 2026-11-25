using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EzCompany.Core.LanAgent.Collaboration;

/// <summary>
/// Background P2P coordinator that delivers queued internal chat messages, synchronizes
/// shared timeline plans, and pulls/pushes updated company logos across online LAN peers.
/// </summary>
public sealed class PeerCollabSyncService : BackgroundService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILanDiscoveryService _discovery;
    private readonly LanCollaborationClient _collabClient;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly ILogger<PeerCollabSyncService> _logger;

    public PeerCollabSyncService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILanDiscoveryService discovery,
        LanCollaborationClient collabClient,
        ILocalStoragePaths paths,
        ISessionContext session,
        ILogger<PeerCollabSyncService> logger)
    {
        _appDbFactory = appDbFactory;
        _discovery = discovery;
        _collabClient = collabClient;
        _paths = paths;
        _session = session;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(8));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (_session.IsAuthenticated)
                {
                    await SyncCollaborationOnceAsync(null, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task<(int ChatDelivered, int TimelineSynced, bool BrandingUpdated)> SyncCollaborationOnceAsync(
        IReadOnlyDictionary<string, string>? endpointOverrides = null,
        CancellationToken ct = default)
    {
        int chatCount = await FlushPendingChatAsync(endpointOverrides, ct).ConfigureAwait(false);
        int timelineChanges = 0;
        bool brandingUpdated = false;

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsGenesisCompleted)
        {
            return (chatCount, 0, false);
        }

        string companyIdStr = company.CompanyId.ToString("D");
        DiscoveredPeer[] peers = _discovery.GetPeers()
            .Where(p => string.Equals(p.CompanyId, companyIdStr, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (!company.IsDirectorNode)
        {
            try
            {
                brandingUpdated = await _collabClient.PullCompanyBrandingFromDirectorAsync(null, ct).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        foreach (DiscoveredPeer peer in peers)
        {
            try
            {
                timelineChanges += await _collabClient.SyncTimelineWithPeerAsync(peer.Endpoint, ct).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        return (chatCount, timelineChanges, brandingUpdated);
    }

    public async Task<int> FlushPendingChatAsync(
        IReadOnlyDictionary<string, string>? endpointOverrides = null,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsGenesisCompleted)
        {
            return 0;
        }

        List<ChatMessageEntity> pending = await db.ChatMessages
            .Where(m => m.PendingSyncTargetsJson != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return 0;
        }

        Guid[] convIds = pending.Select(m => m.ConversationId).Distinct().ToArray();
        Dictionary<Guid, ChatConversationEntity> convs = await db.ChatConversations.AsNoTracking()
            .Where(c => convIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct)
            .ConfigureAwait(false);

        string companyIdStr = company.CompanyId.ToString("D");
        Dictionary<string, string> peerEndpoints = _discovery.GetPeers()
            .Where(p => string.Equals(p.CompanyId, companyIdStr, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.MachineId, p => p.Endpoint, StringComparer.OrdinalIgnoreCase);

        if (endpointOverrides is not null)
        {
            foreach ((string k, string v) in endpointOverrides)
            {
                peerEndpoints[k] = v;
            }
        }

        int deliveredMsgs = 0;
        foreach (ChatMessageEntity msg in pending)
        {
            if (!convs.TryGetValue(msg.ConversationId, out ChatConversationEntity? conv))
            {
                msg.PendingSyncTargetsJson = null;
                continue;
            }

            string[]? targets;
            try
            {
                targets = JsonSerializer.Deserialize<string[]>(msg.PendingSyncTargetsJson!);
            }
            catch
            {
                msg.PendingSyncTargetsJson = null;
                continue;
            }

            if (targets is null || targets.Length == 0)
            {
                msg.PendingSyncTargetsJson = null;
                continue;
            }

            // Expand ["ALL"] wildcard to all currently known same-company online peers
            string[] resolvedTargets = targets.Length == 1 && string.Equals(targets[0], "ALL", StringComparison.OrdinalIgnoreCase)
                ? peerEndpoints.Keys.Where(k => !string.Equals(k, _paths.MachineId, StringComparison.OrdinalIgnoreCase)).ToArray()
                : targets;

            var remaining = new List<string>();
            bool sentAny = false;

            foreach (string targetId in resolvedTargets)
            {
                if (!peerEndpoints.TryGetValue(targetId, out string? ep))
                {
                    remaining.Add(targetId);
                    continue;
                }

                try
                {
                    await _collabClient.SendChatMessageToPeerAsync(ep, conv, msg, ct).ConfigureAwait(false);
                    sentAny = true;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Deferred chat sync for {MessageId} -> {Target}.", msg.Id, targetId);
                    remaining.Add(targetId);
                }
            }

            if (sentAny)
            {
                deliveredMsgs++;
            }

            msg.PendingSyncTargetsJson = remaining.Count == 0
                ? null
                : JsonSerializer.Serialize(remaining.ToArray());
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return deliveredMsgs;
    }
}
