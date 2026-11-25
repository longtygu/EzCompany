using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Security;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.LanAgent;

public sealed record DownloadedArtifact(byte[] Bytes, string? Sha256Header, int? SchemaVersionHeader, string FileName);

/// <summary>
/// Signed HTTP client used exclusively by Director nodes to manage workstations over LAN.
/// </summary>
public sealed class DirectorLanClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly DirectorRequestSigner _signer;
    private readonly LocalKeyStore _keyStore;
    private readonly ISessionContext _session;
    private readonly EciesCryptoService _ecies;
    private readonly PolicyEngine _policyEngine;
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly TimeProvider _timeProvider;

    public DirectorLanClient(
        IHttpClientFactory httpFactory,
        DirectorRequestSigner signer,
        LocalKeyStore keyStore,
        ISessionContext session,
        EciesCryptoService ecies,
        PolicyEngine policyEngine,
        IDbContextFactory<AppDbContext> appDbFactory,
        TimeProvider? timeProvider = null)
    {
        _httpFactory = httpFactory;
        _signer = signer;
        _keyStore = keyStore;
        _session = session;
        _ecies = ecies;
        _policyEngine = policyEngine;
        _appDbFactory = appDbFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<WorkstationHealthDto> GetHealthAsync(string endpoint, CancellationToken ct = default)
    {
        using HttpResponseMessage resp = await SendSignedAsync(HttpMethod.Get, endpoint, "/api/v1/health", null, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<WorkstationHealthDto>(cancellationToken: ct).ConfigureAwait(false))!;
    }

    public async Task<ApplyPolicyResponseDto> PushPolicyAsync(
        string endpoint,
        string signedPolicyJson,
        CancellationToken ct = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new ApplyPolicyRequestDto(signedPolicyJson));
        using HttpResponseMessage resp = await SendSignedAsync(HttpMethod.Post, endpoint, "/api/v1/policy/apply", body, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ApplyPolicyResponseDto>(cancellationToken: ct).ConfigureAwait(false))!;
    }

    public async Task PushCompanyBrandingAsync(
        string endpoint,
        CompanyBrandingDto dto,
        CancellationToken ct = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(dto);
        using HttpResponseMessage resp = await SendSignedAsync(HttpMethod.Post, endpoint, "/api/v1/company/branding", body, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<VaultFileEntryDto>> GetVaultTreeAsync(string endpoint, CancellationToken ct = default)
    {
        using HttpResponseMessage resp = await SendSignedAsync(HttpMethod.Get, endpoint, "/api/v1/vault/tree", null, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<VaultFileEntryDto>>(cancellationToken: ct).ConfigureAwait(false)
               ?? [];
    }

    public async Task<DownloadedArtifact> DownloadVaultFileAsync(
        string endpoint,
        string relativePath,
        CancellationToken ct = default)
    {
        string pathAndQuery = $"/api/v1/vault/download?relativePath={Uri.EscapeDataString(relativePath)}";
        using HttpResponseMessage resp = await SendSignedAsync(HttpMethod.Get, endpoint, pathAndQuery, null, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        string? shaHeader = resp.Headers.TryGetValues("X-Ez-Sha256", out var shaVals) ? shaVals.FirstOrDefault() : null;
        string actualSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.IsNullOrEmpty(shaHeader) && !string.Equals(shaHeader, actualSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException($"SHA-256 tệp tải về không khớp (kỳ vọng {shaHeader}, nhận {actualSha}).");
        }

        string fileName = resp.Content.Headers.ContentDisposition?.FileNameStar
            ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? Path.GetFileName(relativePath);

        return new DownloadedArtifact(bytes, shaHeader ?? actualSha, null, fileName);
    }

    public async Task<DownloadedArtifact> DownloadDatabaseSnapshotAsync(string endpoint, CancellationToken ct = default)
    {
        using HttpResponseMessage resp = await SendSignedAsync(HttpMethod.Post, endpoint, "/api/v1/database/snapshot", [], ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        string? shaHeader = resp.Headers.TryGetValues("X-Ez-Sha256", out var shaVals) ? shaVals.FirstOrDefault() : null;
        int? schemaVersion = resp.Headers.TryGetValues("X-Ez-Schema-Version", out var verVals) &&
                             int.TryParse(verVals.FirstOrDefault(), out int sv)
            ? sv
            : null;

        string actualSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.IsNullOrEmpty(shaHeader) && !string.Equals(shaHeader, actualSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException("SHA-256 bản chụp DB VACUUM INTO bị sai lệch khi truyền.");
        }

        string fileName = resp.Content.Headers.ContentDisposition?.FileNameStar
            ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"snapshot_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.db";

        return new DownloadedArtifact(bytes, shaHeader ?? actualSha, schemaVersion, fileName);
    }

    public async Task SendNoticeAsync(string endpoint, string title, string message, CancellationToken ct = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(
            new NoticeRequestDto(Guid.NewGuid(), title, message, _timeProvider.GetUtcNow()));
        using HttpResponseMessage resp = await SendSignedAsync(HttpMethod.Post, endpoint, "/api/v1/notice", body, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetRecentAuditsAsync(
        string endpoint,
        int limit = 100,
        CancellationToken ct = default)
    {
        using HttpResponseMessage resp = await SendSignedAsync(
            HttpMethod.Get,
            endpoint,
            $"/api/v1/audit/recent?limit={limit}",
            null,
            ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<AuditEntryDto>>(cancellationToken: ct).ConfigureAwait(false)
               ?? [];
    }

    public async Task<IReadOnlyList<RegistryDeviceSyncDto>> GetDirectorRegistryAsync(
        string endpoint,
        CancellationToken ct = default)
    {
        using HttpResponseMessage resp = await SendSignedAsync(
            HttpMethod.Get,
            endpoint,
            "/api/v1/director/registry",
            null,
            ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<RegistryDeviceSyncDto>>(cancellationToken: ct).ConfigureAwait(false)
               ?? [];
    }

    public async Task PushDirectorRegistryAsync(
        string endpoint,
        IEnumerable<RegistryDeviceSyncDto> entries,
        CancellationToken ct = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(entries.ToList());
        using HttpResponseMessage resp = await SendSignedAsync(
            HttpMethod.Post,
            endpoint,
            "/api/v1/director/registry/sync",
            body,
            ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<TelemetryDeltaResponseDto> PullTelemetryDeltaAsync(
        string endpoint,
        DateTimeOffset sinceUtc,
        CancellationToken ct = default)
    {
        string pathAndQuery = $"/api/v1/telemetry/delta?sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("O"))}";
        using HttpResponseMessage resp = await SendSignedAsync(HttpMethod.Get, endpoint, pathAndQuery, null, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TelemetryDeltaResponseDto>(cancellationToken: ct).ConfigureAwait(false))!;
    }

    public async Task<TelemetryFullSnapshotDto> FetchFullSnapshotAsync(
        string endpoint,
        Guid snapshotId,
        CancellationToken ct = default)
    {
        using HttpResponseMessage resp = await SendSignedAsync(
            HttpMethod.Get,
            endpoint,
            $"/api/v1/telemetry/snapshot/{snapshotId}",
            null,
            ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TelemetryFullSnapshotDto>(cancellationToken: ct).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Issues a new signed policy version for a workstation, pushes immediately if reachable,
    /// or saves to <see cref="PairedDeviceEntity.PendingPolicyJson"/> for background delivery when offline.
    /// </summary>
    public async Task<(bool PushedImmediately, string PolicyJson)> IssueAndDispatchPolicyAsync(
        string targetMachineId,
        string role,
        IReadOnlyList<string> enabledModules,
        string allowedUpdateChannel,
        string maxApprovedVersion,
        string adminName,
        CancellationToken ct = default)
    {
        byte[] rootPriv = _keyStore.LoadRootPrivateKey()
            ?? throw new InvalidOperationException("Không tìm thấy RootPrivateKey để ký phân quyền.");

        try
        {
            await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            PairedDeviceEntity device = await db.PairedDevices
                .FirstOrDefaultAsync(x => x.MachineId == targetMachineId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Không tìm thấy thiết bị {targetMachineId} trong sổ ghép đôi.");

            DateTimeOffset now = _timeProvider.GetUtcNow();
            byte[] encPub = _ecies.DeriveCompanyX25519PublicKey(rootPriv);
            long nextVersion = device.CurrentPolicyVersion + 1;

            var payload = new PolicyPayload(
                CompanyId: device.CompanyId,
                MachineId: device.MachineId,
                DevicePublicKey: device.DevicePublicKeyBase64,
                CompanyEncryptionPublicKey: Convert.ToBase64String(encPub),
                UserId: device.UserId,
                Username: device.Username,
                FullName: device.FullName,
                Role: role,
                EnabledModules: enabledModules.ToArray(),
                EnabledPermissions: CompanyRoles.GetDefaultPermissionsForRole(role),
                PolicyVersion: nextVersion,
                IssuedAtUtc: now,
                ExpiresAtUtc: now.AddDays(PolicyEngine.DefaultValidityDays),
                AllowedUpdateChannel: allowedUpdateChannel,
                MaxApprovedVersion: maxApprovedVersion,
                IssuedByAdminName: adminName);

            string policyJson = _policyEngine.SerializeDocument(_policyEngine.IssueSignedPolicy(payload, rootPriv));
            device.Role = role;
            device.CurrentPolicyVersion = nextVersion;
            device.CurrentPolicyJson = policyJson;
            device.PendingPolicyJson = policyJson;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(device.LastKnownIp) && device.LastKnownPort > 0)
            {
                try
                {
                    await PushPolicyAsync($"{device.LastKnownIp}:{device.LastKnownPort}", policyJson, ct)
                        .ConfigureAwait(false);

                    device.PendingPolicyJson = null;
                    device.IsOnline = true;
                    device.LastSeenUtc = _timeProvider.GetUtcNow();
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    return (true, policyJson);
                }
                catch
                {
                    // Offline or temporarily unreachable: stays in PendingPolicyJson queue
                }
            }

            return (false, policyJson);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootPriv);
        }
    }

    public async Task<HttpResponseMessage> SendSignedAsync(
        HttpMethod method,
        string endpoint,
        string pathAndQuery,
        byte[]? bodyBytes,
        CancellationToken ct = default)
    {
        byte[] rootPriv = _keyStore.LoadRootPrivateKey()
            ?? throw new InvalidOperationException("Chức năng này yêu cầu khóa gốc Root của Giám đốc.");

        try
        {
            string baseUrl = NormalizeBaseUrl(endpoint);
            using var request = new HttpRequestMessage(method, new Uri($"{baseUrl}{pathAndQuery}"));
            if (bodyBytes is not null)
            {
                request.Content = new ByteArrayContent(bodyBytes);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }

            string adminName = string.IsNullOrWhiteSpace(_session.Username) ? "director" : _session.Username;
            _signer.SignHttpRequest(request, bodyBytes, rootPriv, adminName, _timeProvider.GetUtcNow());

            using HttpClient client = _httpFactory.CreateClient("EzLan");
            return await client.SendAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootPriv);
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
