using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Makaretu.Dns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EzCompany.Core.LanAgent.Discovery;

/// <summary>
/// Discovers and advertises Ez Company peers over mDNS (<c>_ezcompany._tcp.local.</c>)
/// plus a UDP Multicast + Broadcast fallback beacon so both physical LANs and multi-<c>--profile</c>
/// instances on a single PC discover each other seamlessly.
/// </summary>
public sealed class LanDiscoveryService : BackgroundService, ILanDiscoveryService
{
    public const string MdnsServiceType = "_ezcompany._tcp";
    public const int FallbackUdpPort = 45821;
    private static readonly IPAddress FallbackMulticastGroup = IPAddress.Parse("239.255.45.82");
    private static readonly TimeSpan PeerStaleTimeout = TimeSpan.FromSeconds(15);

    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IEventBus _eventBus;
    private readonly ILogger<LanDiscoveryService> _logger;

    private readonly ConcurrentDictionary<string, DiscoveredPeer> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _advLock = new();

    private MulticastService? _mdns;
    private ServiceDiscovery? _sd;
    private ServiceProfile? _currentProfile;
    private bool _pairingOpen;
    private CancellationTokenSource? _lifecycleCts;

    private sealed record UdpBeaconPayload(
        string CompanyId,
        string CompanyName,
        string NodeRole,
        string MachineId,
        string PubKeyHash,
        int Port,
        bool PairingOpen,
        string AppVersion,
        string? UserFullName = null,
        string? Username = null,
        string? UserRole = null);

    public LanDiscoveryService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        IEventBus eventBus,
        ILogger<LanDiscoveryService> logger)
    {
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
        _eventBus = eventBus;
        _logger = logger;
    }

    public event EventHandler? PeersChanged;

    public IReadOnlyCollection<DiscoveredPeer> GetPeers() =>
        _peers.Values.OrderBy(p => p.CompanyName).ThenBy(p => p.MachineId).ToArray();

    public IReadOnlyCollection<DiscoveredPeer> GetPairingOpenDirectors() =>
        _peers.Values.Where(p => p.IsDirector && p.PairingOpen).OrderBy(p => p.CompanyName).ToArray();

    public void RefreshAdvertisement(bool? pairingOpenOverride = null)
    {
        if (pairingOpenOverride.HasValue)
        {
            _pairingOpen = pairingOpenOverride.Value;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await AdvertiseCurrentNodeAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "RefreshAdvertisement background error.");
            }
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        StartMdns();

        Task udpListenerTask = RunUdpListenerAsync(_lifecycleCts.Token);
        Task beaconLoopTask = RunBeaconAndExpiryLoopAsync(_lifecycleCts.Token);

        await Task.WhenAll(udpListenerTask, beaconLoopTask).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _lifecycleCts?.Cancel();
        }
        catch
        {
        }

        lock (_advLock)
        {
            try
            {
                if (_sd is not null && _currentProfile is not null)
                {
                    _sd.Unadvertise(_currentProfile);
                }
            }
            catch
            {
            }

            _sd?.Dispose();
            _mdns?.Stop();
            _mdns?.Dispose();
            _sd = null;
            _mdns = null;
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private void StartMdns()
    {
        try
        {
            var mdns = new MulticastService();
            var sd = new ServiceDiscovery(mdns);

            mdns.AnswerReceived += (_, args) => HandleMdnsMessage(args.Message, args.RemoteEndPoint?.Address);
            sd.ServiceInstanceDiscovered += (_, args) =>
            {
                if (args.Message is not null)
                {
                    HandleMdnsMessage(args.Message, args.RemoteEndPoint?.Address);
                }
            };

            mdns.Start();
            sd.QueryServiceInstances(MdnsServiceType);

            lock (_advLock)
            {
                _mdns = mdns;
                _sd = sd;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "mDNS multicast startup failed; continuing with UDP discovery fallback.");
        }
    }

    private void HandleMdnsMessage(Message msg, IPAddress? remoteAddress)
    {
        try
        {
            IEnumerable<ResourceRecord> allRecords = msg.Answers.Concat(msg.AdditionalRecords);
            var txtRecords = allRecords.OfType<TXTRecord>().ToList();
            if (txtRecords.Count == 0)
            {
                return;
            }

            foreach (TXTRecord txt in txtRecords)
            {
                string canonical = txt.Name.ToString();
                if (!canonical.Contains(MdnsServiceType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string entry in txt.Strings)
                {
                    int idx = entry.IndexOf('=');
                    if (idx > 0)
                    {
                        map[entry[..idx]] = entry[(idx + 1)..];
                    }
                }

                if (!map.TryGetValue("machine_id", out string? machineId) ||
                    string.Equals(machineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int port = map.TryGetValue("port", out string? portStr) && int.TryParse(portStr, out int p) ? p : 0;
                if (port <= 0)
                {
                    port = allRecords.OfType<SRVRecord>().FirstOrDefault()?.Port ?? 0;
                }

                if (port <= 0)
                {
                    continue;
                }

                IPAddress? aRecordIp = allRecords.OfType<ARecord>().FirstOrDefault()?.Address;
                string ipStr = (aRecordIp ?? remoteAddress ?? IPAddress.Loopback).ToString();

                var peer = new DiscoveredPeer(
                    CompanyId: map.GetValueOrDefault("company_id", string.Empty),
                    CompanyName: map.GetValueOrDefault("company_name", string.Empty),
                    NodeRole: map.GetValueOrDefault("node_role", "employee"),
                    MachineId: machineId,
                    PubKeyHash: map.GetValueOrDefault("pubkey_hash", string.Empty),
                    IpAddress: ipStr,
                    Port: port,
                    PairingOpen: map.TryGetValue("pairing_open", out string? po) && bool.TryParse(po, out bool b) && b,
                    AppVersion: map.GetValueOrDefault("app_version", "0.1.0"),
                    LastSeenUtc: DateTimeOffset.UtcNow,
                    UserFullName: map.GetValueOrDefault("user_fullname", string.Empty),
                    Username: map.GetValueOrDefault("username", string.Empty),
                    UserRole: map.GetValueOrDefault("user_role", string.Empty));

                UpsertPeer(peer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignored malformed mDNS packet.");
        }
    }

    private async Task RunUdpListenerAsync(CancellationToken ct)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, FallbackUdpPort));
            try
            {
                socket.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.AddMembership,
                    new MulticastOption(FallbackMulticastGroup, IPAddress.Any));
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Multicast group join skipped; broadcast listener remains active.");
            }

            byte[] buffer = new byte[4096];
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            while (!ct.IsCancellationRequested)
            {
                SocketReceiveFromResult result = await socket
                    .ReceiveFromAsync(buffer, SocketFlags.None, remoteEp, ct)
                    .ConfigureAwait(false);

                if (result.ReceivedBytes <= 0)
                {
                    continue;
                }

                try
                {
                    UdpBeaconPayload? beacon = JsonSerializer.Deserialize<UdpBeaconPayload>(
                        buffer.AsSpan(0, result.ReceivedBytes));
                    if (beacon is null ||
                        beacon.Port <= 0 ||
                        string.IsNullOrWhiteSpace(beacon.MachineId) ||
                        string.Equals(beacon.MachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var ep = (IPEndPoint)result.RemoteEndPoint;
                    IPAddress senderIp = ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4() : ep.Address;

                    UpsertPeer(new DiscoveredPeer(
                        beacon.CompanyId,
                        beacon.CompanyName,
                        beacon.NodeRole,
                        beacon.MachineId,
                        beacon.PubKeyHash,
                        senderIp.ToString(),
                        beacon.Port,
                        beacon.PairingOpen,
                        beacon.AppVersion,
                        DateTimeOffset.UtcNow,
                        beacon.UserFullName ?? string.Empty,
                        beacon.Username ?? string.Empty,
                        beacon.UserRole ?? string.Empty));
                }
                catch
                {
                    // Ignore non-EzCompany UDP traffic on shared port
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UDP discovery listener exited.");
        }
    }

    private async Task RunBeaconAndExpiryLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            await AdvertiseCurrentNodeAsync(ct).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await AdvertiseCurrentNodeAsync(ct).ConfigureAwait(false);
                ExpireStalePeers();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task AdvertiseCurrentNodeAsync(CancellationToken ct)
    {
        int port = _session.CurrentLanPort;
        if (port <= 0)
        {
            return;
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsGenesisCompleted)
        {
            return;
        }

        string pubKeyHash = ComputePubKeyHash(company.RootPublicKeyBase64);
        string nodeRole = company.IsDirectorNode ? "director" : "employee";
        string companyIdStr = company.CompanyId.ToString("D");

        LocalUserEntity? localUser = await db.LocalUsers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive, ct)
            .ConfigureAwait(false);
        string userFullName = _session.IsAuthenticated && !string.IsNullOrWhiteSpace(_session.FullName)
            ? _session.FullName
            : localUser?.FullName ?? string.Empty;
        string username = _session.IsAuthenticated && !string.IsNullOrWhiteSpace(_session.Username)
            ? _session.Username
            : localUser?.Username ?? string.Empty;
        string userRole = _session.IsAuthenticated && !string.IsNullOrWhiteSpace(_session.Role)
            ? _session.Role
            : localUser?.Role ?? (company.IsDirectorNode ? CompanyRoles.Director : CompanyRoles.Staff);

        // 1. Update mDNS profile
        lock (_advLock)
        {
            if (_sd is not null)
            {
                try
                {
                    var profile = new ServiceProfile(
                        instanceName: _paths.MachineId,
                        serviceName: MdnsServiceType,
                        port: (ushort)port,
                        addresses: GetAdvertiseAddresses());

                    profile.AddProperty("company_id", companyIdStr);
                    profile.AddProperty("company_name", company.CompanyName);
                    profile.AddProperty("node_role", nodeRole);
                    profile.AddProperty("machine_id", _paths.MachineId);
                    profile.AddProperty("pubkey_hash", pubKeyHash);
                    profile.AddProperty("port", port.ToString());
                    profile.AddProperty("pairing_open", _pairingOpen ? "true" : "false");
                    profile.AddProperty("app_version", _session.CurrentAppVersion);
                    profile.AddProperty("user_fullname", userFullName);
                    profile.AddProperty("username", username);
                    profile.AddProperty("user_role", userRole);

                    if (_currentProfile is not null)
                    {
                        _sd.Unadvertise(_currentProfile);
                    }

                    _currentProfile = profile;
                    _sd.Advertise(profile);
                    _sd.Announce(profile);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "mDNS advertise skipped.");
                }
            }
        }

        // 2. Send UDP Multicast + Broadcast fallback beacon
        try
        {
            var payload = new UdpBeaconPayload(
                companyIdStr,
                company.CompanyName,
                nodeRole,
                _paths.MachineId,
                pubKeyHash,
                port,
                _pairingOpen,
                _session.CurrentAppVersion,
                userFullName,
                username,
                userRole);

            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            using var udp = new UdpClient { EnableBroadcast = true };
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

            await udp.SendAsync(bytes, new IPEndPoint(FallbackMulticastGroup, FallbackUdpPort), ct).ConfigureAwait(false);
            try
            {
                await udp.SendAsync(bytes, new IPEndPoint(IPAddress.Broadcast, FallbackUdpPort), ct).ConfigureAwait(false);
            }
            catch
            {
                // Broadcast can be unavailable on loopback-only hosts
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UDP beacon send failed.");
        }
    }

    private void UpsertPeer(DiscoveredPeer peer)
    {
        bool isNew = !_peers.ContainsKey(peer.MachineId);
        _peers[peer.MachineId] = peer;

        PeersChanged?.Invoke(this, EventArgs.Empty);
        if (isNew)
        {
            _eventBus.Publish(new PeerStatusChangedEvent(
                peer.MachineId,
                peer.CompanyId,
                peer.NodeRole,
                peer.IpAddress,
                peer.Port,
                IsOnline: true));
        }
    }

    private void ExpireStalePeers()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - PeerStaleTimeout;
        bool removedAny = false;

        foreach ((string key, DiscoveredPeer peer) in _peers)
        {
            if (peer.LastSeenUtc < cutoff && _peers.TryRemove(key, out _))
            {
                removedAny = true;
                _eventBus.Publish(new PeerStatusChangedEvent(
                    peer.MachineId,
                    peer.CompanyId,
                    peer.NodeRole,
                    peer.IpAddress,
                    peer.Port,
                    IsOnline: false));
            }
        }

        if (removedAny)
        {
            PeersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static IEnumerable<IPAddress> GetAdvertiseAddresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                    {
                        list.Add(addr.Address);
                    }
                }
            }
        }
        catch
        {
        }

        if (list.Count == 0)
        {
            list.Add(IPAddress.Loopback);
        }

        return list;
    }

    private static string ComputePubKeyHash(string rootPublicKeyBase64)
    {
        byte[] raw = Convert.FromBase64String(rootPublicKeyBase64);
        return Convert.ToHexString(SHA256.HashData(raw)[..8]);
    }
}
