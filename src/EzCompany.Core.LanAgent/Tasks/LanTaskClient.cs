using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Discovery;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Security;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.LanAgent.Tasks;

/// <summary>
/// Device-signed peer-to-peer HTTP client for task assignment, handover submission,
/// profile name updates, and company peer directory discovery.
/// </summary>
public sealed class LanTaskClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly LocalKeyStore _keyStore;
    private readonly ILanDiscoveryService _discovery;
    private readonly DeviceRequestSigner _deviceSigner;

    public LanTaskClient(
        IHttpClientFactory httpFactory,
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        LocalKeyStore keyStore,
        ILanDiscoveryService discovery,
        DeviceRequestSigner deviceSigner)
    {
        _httpFactory = httpFactory;
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
        _keyStore = keyStore;
        _discovery = discovery;
        _deviceSigner = deviceSigner;
    }

    public async Task<IReadOnlyList<PeerDirectoryEntryDto>> GetCompanyPeerDirectoryAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null)
        {
            return [];
        }

        string companyIdStr = company.CompanyId.ToString("D");
        var byMachineId = new Dictionary<string, PeerDirectoryEntryDto>(StringComparer.OrdinalIgnoreCase);

        // 1. Always include self so users can also assign/delegate cleanly
        if (_session.IsAuthenticated)
        {
            byMachineId[_paths.MachineId] = new PeerDirectoryEntryDto(
                _paths.MachineId,
                _session.Username,
                $"{_session.FullName} (Máy này)",
                _session.Role,
                true,
                null);
        }

        // 2. If Director node, seed from local PairedDevices registry
        if (company.IsDirectorNode)
        {
            List<PairedDeviceEntity> localDevices = await db.PairedDevices.AsNoTracking()
                .Where(d => d.CompanyId == company.CompanyId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (PairedDeviceEntity dev in localDevices)
            {
                if (string.Equals(dev.MachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? ep = !string.IsNullOrWhiteSpace(dev.LastKnownIp) && dev.LastKnownPort > 0
                    ? $"{dev.LastKnownIp}:{dev.LastKnownPort}"
                    : null;

                byMachineId[dev.MachineId] = new PeerDirectoryEntryDto(
                    dev.MachineId,
                    dev.Username,
                    dev.FullName,
                    dev.Role,
                    dev.IsOnline,
                    ep);
            }
        }

        // 3. Query an online Director if available (gives Employee nodes full colleague roster including offline peers)
        DiscoveredPeer? onlineDirector = _discovery.GetPeers()
            .FirstOrDefault(p => p.IsDirector && string.Equals(p.CompanyId, companyIdStr, StringComparison.OrdinalIgnoreCase));

        if (onlineDirector is not null && !company.IsDirectorNode)
        {
            try
            {
                IReadOnlyList<PeerDirectoryEntryDto> remoteList = await FetchDirectoryFromDirectorAsync(
                    onlineDirector.Endpoint,
                    company.CompanyId,
                    ct).ConfigureAwait(false);

                foreach (PeerDirectoryEntryDto entry in remoteList)
                {
                    if (!string.Equals(entry.MachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
                    {
                        byMachineId[entry.MachineId] = entry;
                    }
                }
            }
            catch
            {
                // Best effort; fallback cleanly to live LAN discovery beacons below
            }
        }

        // 4. Overlay live LAN discovery peers for freshest online endpoints & display names
        foreach (DiscoveredPeer peer in _discovery.GetPeers()
                     .Where(p => string.Equals(p.CompanyId, companyIdStr, StringComparison.OrdinalIgnoreCase)))
        {
            byMachineId.TryGetValue(peer.MachineId, out PeerDirectoryEntryDto? existing);
            string fullName = !string.IsNullOrWhiteSpace(peer.UserFullName)
                ? peer.UserFullName
                : existing?.FullName ?? peer.MachineId;
            string username = !string.IsNullOrWhiteSpace(peer.Username)
                ? peer.Username
                : existing?.Username ?? peer.MachineId.ToLowerInvariant();
            string role = !string.IsNullOrWhiteSpace(peer.UserRole)
                ? peer.UserRole
                : existing?.Role ?? (peer.IsDirector ? CompanyRoles.Director : CompanyRoles.Staff);

            byMachineId[peer.MachineId] = new PeerDirectoryEntryDto(
                peer.MachineId,
                username,
                fullName,
                role,
                true,
                peer.Endpoint);
        }

        return byMachineId.Values
            .OrderByDescending(p => p.IsOnline)
            .ThenBy(p => p.FullName)
            .ToArray();
    }

    public async Task<TaskSyncAckDto> SendTaskSyncAsync(
        string targetEndpoint,
        WorkTaskEntity task,
        string syncAction,
        string historyNote,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(targetEndpoint))
        {
            throw new ArgumentException("Thiếu địa chỉ LAN của máy nhận.", nameof(targetEndpoint));
        }

        byte[] devicePriv = _keyStore.LoadDevicePrivateKey()
            ?? throw new InvalidOperationException("Chưa có khóa riêng thiết bị.");
        byte[] devicePub = _keyStore.LoadDevicePublicKey()
            ?? throw new InvalidOperationException("Chưa có khóa công khai thiết bị.");
        string senderPolicyJson = _keyStore.LoadPolicyLicenseJson()
            ?? throw new InvalidOperationException("Chưa có giấy phép policy.lic cục bộ.");

        string? fileBase64 = null;
        if (!string.IsNullOrWhiteSpace(task.HandoverVaultPath))
        {
            string fullPath = Path.GetFullPath(Path.Combine(_paths.VaultDirectory, task.HandoverVaultPath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(fullPath))
            {
                byte[] bytes = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
                fileBase64 = Convert.ToBase64String(bytes);
            }
        }

        string? assignmentFileBase64 = null;
        if (!string.IsNullOrWhiteSpace(task.AssignmentVaultPath))
        {
            string fullAssignPath = Path.GetFullPath(Path.Combine(_paths.VaultDirectory, task.AssignmentVaultPath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(fullAssignPath))
            {
                byte[] assignBytes = await File.ReadAllBytesAsync(fullAssignPath, ct).ConfigureAwait(false);
                assignmentFileBase64 = Convert.ToBase64String(assignBytes);
            }
        }

        try
        {
            var payload = new TaskSyncPayloadDto(
                TaskId: task.Id,
                CompanyId: task.CompanyId,
                TaskKind: task.TaskKind,
                Title: task.Title,
                Description: task.Description,
                Priority: task.Priority,
                Status: task.Status,
                AssignerMachineId: task.AssignerMachineId,
                AssignerUsername: task.AssignerUsername,
                AssignerFullName: task.AssignerFullName,
                AssignerRole: task.AssignerRole,
                AssigneeMachineId: task.AssigneeMachineId,
                AssigneeUsername: task.AssigneeUsername,
                AssigneeFullName: task.AssigneeFullName,
                AssigneeRole: task.AssigneeRole,
                DueDateUtc: task.DueDateUtc,
                CreatedAtUtc: task.CreatedAtUtc,
                UpdatedAtUtc: task.UpdatedAtUtc,
                CompletedAtUtc: task.CompletedAtUtc,
                HandoverNotes: task.HandoverNotes,
                HandoverFileName: task.HandoverFileName,
                HandoverFileSha256: task.HandoverFileSha256,
                HandoverFileBase64: fileBase64,
                HandedOverAtUtc: task.HandedOverAtUtc,
                ReviewNotes: task.ReviewNotes,
                SyncAction: syncAction,
                HistoryNote: historyNote,
                SenderSignedPolicyJson: senderPolicyJson,
                AssignmentFileName: task.AssignmentFileName,
                AssignmentFileSha256: task.AssignmentFileSha256,
                AssignmentFileBase64: assignmentFileBase64);

            string baseUrl = NormalizeBaseUrl(targetEndpoint);
            byte[] bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/tasks/sync")
            {
                Content = new ByteArrayContent(bodyBytes)
            };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            _deviceSigner.SignHttpRequest(req, bodyBytes, devicePriv, devicePub, task.CompanyId, _paths.MachineId);

            using HttpClient client = _httpFactory.CreateClient("EzLan");
            using HttpResponseMessage resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Đồng bộ công việc thất bại ({(int)resp.StatusCode}): {err}");
            }

            return await resp.Content.ReadFromJsonAsync<TaskSyncAckDto>(cancellationToken: ct).ConfigureAwait(false)
                ?? new TaskSyncAckDto(true, "Đã đồng bộ.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(devicePriv);
        }
    }

    public async Task NotifyProfileUpdateToDirectorsAsync(
        string newUsername,
        string newFullName,
        CancellationToken ct = default)
    {
        if (_session.CompanyId == Guid.Empty)
        {
            return;
        }

        byte[]? devicePriv = _keyStore.LoadDevicePrivateKey();
        byte[]? devicePub = _keyStore.LoadDevicePublicKey();
        if (devicePriv is null || devicePub is null)
        {
            return;
        }

        string companyIdStr = _session.CompanyId.ToString("D");
        DiscoveredPeer[] directors = _discovery.GetPeers()
            .Where(p => p.IsDirector && string.Equals(p.CompanyId, companyIdStr, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        try
        {
            var dto = new ProfileUpdateNotificationDto(_paths.MachineId, newUsername, newFullName);
            byte[] bodyBytes = JsonSerializer.SerializeToUtf8Bytes(dto);

            foreach (DiscoveredPeer director in directors)
            {
                try
                {
                    string baseUrl = NormalizeBaseUrl(director.Endpoint);
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/peers/profile-update")
                    {
                        Content = new ByteArrayContent(bodyBytes)
                    };
                    req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    _deviceSigner.SignHttpRequest(req, bodyBytes, devicePriv, devicePub, _session.CompanyId, _paths.MachineId);

                    using HttpClient client = _httpFactory.CreateClient("EzLan");
                    using HttpResponseMessage _ = await client.SendAsync(req, ct).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(devicePriv);
        }
    }

    private async Task<IReadOnlyList<PeerDirectoryEntryDto>> FetchDirectoryFromDirectorAsync(
        string directorEndpoint,
        Guid companyId,
        CancellationToken ct)
    {
        byte[]? devicePriv = _keyStore.LoadDevicePrivateKey();
        byte[]? devicePub = _keyStore.LoadDevicePublicKey();
        if (devicePriv is null || devicePub is null)
        {
            return [];
        }

        try
        {
            string baseUrl = NormalizeBaseUrl(directorEndpoint);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/peers/directory");
            _deviceSigner.SignHttpRequest(req, null, devicePriv, devicePub, companyId, _paths.MachineId);

            using HttpClient client = _httpFactory.CreateClient("EzLan");
            using HttpResponseMessage resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            return await resp.Content.ReadFromJsonAsync<List<PeerDirectoryEntryDto>>(cancellationToken: ct).ConfigureAwait(false)
                ?? [];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(devicePriv);
        }
    }

    private static string NormalizeBaseUrl(string endpoint)
    {
        string trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"http://{trimmed}";
    }
}
