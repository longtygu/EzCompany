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

namespace EzCompany.Core.LanAgent.Collaboration;

/// <summary>
/// Device-signed P2P HTTP client for internal chat delivery + reverse history/attachment retrieval,
/// shared timeline plan sync, company logo branding pull/push, and HR attendance submission.
/// </summary>
public sealed class LanCollaborationClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly LocalKeyStore _keyStore;
    private readonly ILanDiscoveryService _discovery;
    private readonly DeviceRequestSigner _deviceSigner;
    private readonly DirectorLanClient _directorClient;
    private readonly CompanyBrandingService _branding;
    private readonly ChatService _chat;
    private readonly TimelinePlanService _timeline;

    public LanCollaborationClient(
        IHttpClientFactory httpFactory,
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        LocalKeyStore keyStore,
        ILanDiscoveryService discovery,
        DeviceRequestSigner deviceSigner,
        DirectorLanClient directorClient,
        CompanyBrandingService branding,
        ChatService chat,
        TimelinePlanService timeline)
    {
        _httpFactory = httpFactory;
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
        _keyStore = keyStore;
        _discovery = discovery;
        _deviceSigner = deviceSigner;
        _directorClient = directorClient;
        _branding = branding;
        _chat = chat;
        _timeline = timeline;
    }

    public async Task<int> PushCompanyBrandingToOnlinePeersAsync(CancellationToken ct = default)
    {
        if (!_keyStore.HasRootPrivateKey)
        {
            return 0;
        }

        (string? logoB64, DateTimeOffset? updatedAt, Guid companyId) = await _branding
            .GetCurrentBrandingTupleAsync(ct)
            .ConfigureAwait(false);
        if (companyId == Guid.Empty)
        {
            return 0;
        }

        var dto = new CompanyBrandingDto(companyId, logoB64, updatedAt ?? DateTimeOffset.UtcNow);
        string companyIdStr = companyId.ToString("D");
        int pushed = 0;

        foreach (DiscoveredPeer peer in _discovery.GetPeers()
                     .Where(p => string.Equals(p.CompanyId, companyIdStr, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                await _directorClient.PushCompanyBrandingAsync(peer.Endpoint, dto, ct).ConfigureAwait(false);
                pushed++;
            }
            catch
            {
            }
        }

        return pushed;
    }

    public async Task<bool> PullCompanyBrandingFromDirectorAsync(string? overrideEndpoint = null, CancellationToken ct = default)
    {
        (Guid companyId, _) = await GetCompanyIdAsync(ct).ConfigureAwait(false);
        if (companyId == Guid.Empty)
        {
            return false;
        }

        string? endpoint = overrideEndpoint ?? _discovery.GetPeers()
            .FirstOrDefault(p => p.IsDirector && string.Equals(p.CompanyId, companyId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            ?.Endpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        CompanyBrandingDto? remote = await SendDeviceGetAsync<CompanyBrandingDto>(
            endpoint,
            "/api/v1/company/branding",
            companyId,
            ct).ConfigureAwait(false);

        if (remote is null)
        {
            return false;
        }

        return await _branding.ApplyIncomingLogoSyncAsync(
            remote.CompanyId,
            remote.CompanyLogoBase64,
            remote.UpdatedAtUtc,
            ct).ConfigureAwait(false);
    }

    public async Task SendChatMessageToPeerAsync(
        string targetEndpoint,
        ChatConversationEntity conv,
        ChatMessageEntity msg,
        CancellationToken ct = default)
    {
        string senderPolicyJson = _keyStore.LoadPolicyLicenseJson()
            ?? throw new InvalidOperationException("Chưa có policy.lic cục bộ.");

        string? attachmentBase64 = null;
        if (!string.IsNullOrWhiteSpace(msg.AttachmentVaultPath))
        {
            string fullPath = Path.GetFullPath(Path.Combine(_paths.VaultDirectory, msg.AttachmentVaultPath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(fullPath))
            {
                attachmentBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false));
            }
        }

        var envelope = new ChatSyncEnvelopeDto(
            ConversationId: conv.Id,
            CompanyId: conv.CompanyId,
            ConversationType: conv.ConversationType,
            ConversationName: conv.Name,
            MemberMachineIdsJson: conv.MemberMachineIdsJson,
            MemberNamesJson: conv.MemberNamesJson,
            PinnedTaskId: conv.PinnedTaskId,
            PinnedTaskSummary: conv.PinnedTaskSummary,
            MessageId: msg.Id,
            SenderMachineId: msg.SenderMachineId,
            SenderUsername: msg.SenderUsername,
            SenderFullName: msg.SenderFullName,
            Content: msg.Content,
            AttachmentFileName: msg.AttachmentFileName,
            AttachmentSha256: msg.AttachmentSha256,
            AttachmentBase64: attachmentBase64,
            AttachmentSizeBytes: msg.AttachmentSizeBytes,
            LinkedTaskId: msg.LinkedTaskId,
            SentAtUtc: msg.SentAtUtc,
            SenderSignedPolicyJson: senderPolicyJson);

        using HttpResponseMessage resp = await SendDevicePostAsync(
            targetEndpoint,
            "/api/v1/chat/sync",
            envelope,
            conv.CompanyId,
            ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<int> PullChatHistoryFromPeerAsync(
        string peerEndpoint,
        DateTimeOffset? sinceUtc = null,
        CancellationToken ct = default)
    {
        (Guid companyId, _) = await GetCompanyIdAsync(ct).ConfigureAwait(false);
        if (companyId == Guid.Empty)
        {
            return 0;
        }

        DateTimeOffset since = sinceUtc ?? DateTimeOffset.UtcNow.AddDays(-14);
        string path = $"/api/v1/chat/history?sinceUtc={Uri.EscapeDataString(since.ToString("O"))}";
        ChatHistoryPullResponseDto? resp = await SendDeviceGetAsync<ChatHistoryPullResponseDto>(peerEndpoint, path, companyId, ct)
            .ConfigureAwait(false);

        if (resp is null || resp.Messages.Count == 0)
        {
            return 0;
        }

        int inserted = 0;
        foreach (ChatSyncEnvelopeDto item in resp.Messages)
        {
            byte[]? attachBytes = string.IsNullOrWhiteSpace(item.AttachmentBase64)
                ? null
                : Convert.FromBase64String(item.AttachmentBase64);

            var conv = new ChatConversationEntity
            {
                Id = item.ConversationId,
                CompanyId = item.CompanyId,
                ConversationType = item.ConversationType,
                Name = item.ConversationName,
                MemberMachineIdsJson = item.MemberMachineIdsJson,
                MemberNamesJson = item.MemberNamesJson,
                PinnedTaskId = item.PinnedTaskId,
                PinnedTaskSummary = item.PinnedTaskSummary,
                CreatedAtUtc = item.SentAtUtc,
                UpdatedAtUtc = item.SentAtUtc
            };

            var msg = new ChatMessageEntity
            {
                Id = item.MessageId,
                ConversationId = item.ConversationId,
                CompanyId = item.CompanyId,
                SenderMachineId = item.SenderMachineId,
                SenderUsername = item.SenderUsername,
                SenderFullName = item.SenderFullName,
                Content = item.Content,
                AttachmentFileName = item.AttachmentFileName,
                AttachmentSha256 = item.AttachmentSha256,
                AttachmentSizeBytes = item.AttachmentSizeBytes,
                LinkedTaskId = item.LinkedTaskId,
                SentAtUtc = item.SentAtUtc
            };

            if (await _chat.UpsertIncomingChatSyncAsync(conv, msg, attachBytes, ct).ConfigureAwait(false))
            {
                inserted++;
            }
        }

        return inserted;
    }

    public async Task<bool> ReverseDownloadChatAttachmentAsync(
        Guid messageId,
        string? preferredEndpoint = null,
        CancellationToken ct = default)
    {
        (Guid companyId, _) = await GetCompanyIdAsync(ct).ConfigureAwait(false);
        if (companyId == Guid.Empty)
        {
            return false;
        }

        List<string> candidates = [];
        if (!string.IsNullOrWhiteSpace(preferredEndpoint))
        {
            candidates.Add(preferredEndpoint);
        }

        foreach (DiscoveredPeer peer in _discovery.GetPeers()
                     .Where(p => string.Equals(p.CompanyId, companyId.ToString("D"), StringComparison.OrdinalIgnoreCase)))
        {
            if (!candidates.Contains(peer.Endpoint, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(peer.Endpoint);
            }
        }

        foreach (string ep in candidates)
        {
            try
            {
                byte[]? bytes = await DownloadBytesDeviceSignedAsync(
                    ep,
                    $"/api/v1/chat/attachment/{messageId:D}",
                    companyId,
                    ct).ConfigureAwait(false);

                if (bytes is { Length: > 0 })
                {
                    await _chat.SaveDownloadedAttachmentToLocalVaultAsync(messageId, bytes, ct).ConfigureAwait(false);
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    public async Task<int> SyncTimelineWithPeerAsync(string peerEndpoint, CancellationToken ct = default)
    {
        (Guid companyId, _) = await GetCompanyIdAsync(ct).ConfigureAwait(false);
        if (companyId == Guid.Empty)
        {
            return 0;
        }

        string? policyJson = _keyStore.LoadPolicyLicenseJson();
        if (string.IsNullOrWhiteSpace(policyJson))
        {
            return 0;
        }

        IReadOnlyList<TimelinePlanEntity> localPlans = await _timeline.GetAllForSyncAsync(ct).ConfigureAwait(false);
        var reqDto = new TimelineSyncRequestDto(
            localPlans.Select(p => new TimelinePlanDto(
                p.Id, p.CompanyId, p.Title, p.Description, p.Category,
                p.StartDateUtc, p.EndDateUtc, p.Status,
                p.CreatedByMachineId, p.CreatedByUsername, p.CreatedByFullName,
                p.CreatedAtUtc, p.UpdatedAtUtc, p.IsDeleted)).ToArray(),
            policyJson);

        using HttpResponseMessage syncResp = await SendDevicePostAsync(
            peerEndpoint,
            "/api/v1/timeline/sync",
            reqDto,
            companyId,
            ct).ConfigureAwait(false);

        int pushedChanges = 0;
        if (syncResp.IsSuccessStatusCode)
        {
            try
            {
                using JsonDocument doc = await JsonDocument.ParseAsync(
                    await syncResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
                    cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("changed", out JsonElement chProp) && chProp.TryGetInt32(out int c))
                {
                    pushedChanges = c;
                }
            }
            catch
            {
            }
        }

        List<TimelinePlanDto>? remotePlans = await SendDeviceGetAsync<List<TimelinePlanDto>>(
            peerEndpoint,
            "/api/v1/timeline/entries",
            companyId,
            ct).ConfigureAwait(false);

        if (remotePlans is null || remotePlans.Count == 0)
        {
            return pushedChanges;
        }

        int pulledChanges = await _timeline.UpsertIncomingPlansAsync(
            remotePlans.Select(r => new TimelinePlanEntity
            {
                Id = r.Id,
                CompanyId = r.CompanyId,
                Title = r.Title,
                Description = r.Description,
                Category = r.Category,
                StartDateUtc = r.StartDateUtc,
                EndDateUtc = r.EndDateUtc,
                Status = r.Status,
                CreatedByMachineId = r.CreatedByMachineId,
                CreatedByUsername = r.CreatedByUsername,
                CreatedByFullName = r.CreatedByFullName,
                CreatedAtUtc = r.CreatedAtUtc,
                UpdatedAtUtc = r.UpdatedAtUtc,
                IsDeleted = r.IsDeleted
            }),
            ct).ConfigureAwait(false);

        return pushedChanges + pulledChanges;
    }

    public async Task<int> PushAttendanceToDirectorsAsync(
        AttendanceRecordEntity record,
        string? overrideEndpoint = null,
        CancellationToken ct = default)
    {
        string? policyJson = _keyStore.LoadPolicyLicenseJson();
        if (string.IsNullOrWhiteSpace(policyJson))
        {
            return 0;
        }

        var dto = new AttendanceSyncDto(
            record.Id,
            record.CompanyId,
            record.EmployeeCode,
            record.EmployeeName,
            record.MachineId,
            record.DateKey,
            record.CheckInUtc,
            record.CheckOutUtc,
            record.WorkedHours,
            record.Status,
            record.Notes,
            record.UpdatedAtUtc,
            policyJson);

        List<string> endpoints = [];
        if (!string.IsNullOrWhiteSpace(overrideEndpoint))
        {
            endpoints.Add(overrideEndpoint);
        }
        else
        {
            string companyIdStr = record.CompanyId.ToString("D");
            endpoints.AddRange(_discovery.GetPeers()
                .Where(p => p.IsDirector && string.Equals(p.CompanyId, companyIdStr, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Endpoint));
        }

        int sent = 0;
        foreach (string ep in endpoints)
        {
            try
            {
                using HttpResponseMessage resp = await SendDevicePostAsync(
                    ep,
                    "/api/v1/hr/attendance/sync",
                    dto,
                    record.CompanyId,
                    ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    sent++;
                }
            }
            catch
            {
            }
        }

        return sent;
    }

    private async Task<(Guid CompanyId, bool IsDirector)> GetCompanyIdAsync(CancellationToken ct)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return company is null ? (Guid.Empty, false) : (company.CompanyId, company.IsDirectorNode);
    }

    private async Task<HttpResponseMessage> SendDevicePostAsync<T>(
        string endpoint,
        string pathAndQuery,
        T payload,
        Guid companyId,
        CancellationToken ct)
    {
        byte[] devicePriv = _keyStore.LoadDevicePrivateKey()
            ?? throw new InvalidOperationException("Chưa có khóa riêng thiết bị.");
        byte[] devicePub = _keyStore.LoadDevicePublicKey()
            ?? throw new InvalidOperationException("Chưa có khóa công khai thiết bị.");

        try
        {
            string baseUrl = NormalizeBaseUrl(endpoint);
            byte[] bodyBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{pathAndQuery}")
            {
                Content = new ByteArrayContent(bodyBytes)
            };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            _deviceSigner.SignHttpRequest(req, bodyBytes, devicePriv, devicePub, companyId, _paths.MachineId);

            using HttpClient client = _httpFactory.CreateClient("EzLan");
            return await client.SendAsync(req, ct).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(devicePriv);
        }
    }

    private async Task<T?> SendDeviceGetAsync<T>(
        string endpoint,
        string pathAndQuery,
        Guid companyId,
        CancellationToken ct)
    {
        byte[] devicePriv = _keyStore.LoadDevicePrivateKey()
            ?? throw new InvalidOperationException("Chưa có khóa riêng thiết bị.");
        byte[] devicePub = _keyStore.LoadDevicePublicKey()
            ?? throw new InvalidOperationException("Chưa có khóa công khai thiết bị.");

        try
        {
            string baseUrl = NormalizeBaseUrl(endpoint);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{pathAndQuery}");
            _deviceSigner.SignHttpRequest(req, null, devicePriv, devicePub, companyId, _paths.MachineId);

            using HttpClient client = _httpFactory.CreateClient("EzLan");
            using HttpResponseMessage resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return default;
            }

            return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(devicePriv);
        }
    }

    private async Task<byte[]?> DownloadBytesDeviceSignedAsync(
        string endpoint,
        string pathAndQuery,
        Guid companyId,
        CancellationToken ct)
    {
        byte[] devicePriv = _keyStore.LoadDevicePrivateKey()
            ?? throw new InvalidOperationException("Chưa có khóa riêng thiết bị.");
        byte[] devicePub = _keyStore.LoadDevicePublicKey()
            ?? throw new InvalidOperationException("Chưa có khóa công khai thiết bị.");

        try
        {
            string baseUrl = NormalizeBaseUrl(endpoint);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{pathAndQuery}");
            _deviceSigner.SignHttpRequest(req, null, devicePriv, devicePub, companyId, _paths.MachineId);

            using HttpClient client = _httpFactory.CreateClient("EzLan");
            using HttpResponseMessage resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
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
