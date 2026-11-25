using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using EzCompany.Core.LanAgent.Models;
using EzCompany.Core.LanAgent.Security;

namespace EzCompany.Core.LanAgent.Recovery;

/// <summary>
/// Employee workstation client for submitting device-signed password reset requests to an online
/// Director machine, waiting for approval, and applying the Root-signed reset ticket locally.
/// </summary>
public sealed class LanAccountRecoveryClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly LocalKeyStore _keyStore;
    private readonly DeviceRequestSigner _deviceSigner;
    private readonly GenesisBootstrapService _bootstrap;

    public LanAccountRecoveryClient(
        IHttpClientFactory httpFactory,
        ILocalStoragePaths paths,
        LocalKeyStore keyStore,
        DeviceRequestSigner deviceSigner,
        GenesisBootstrapService bootstrap)
    {
        _httpFactory = httpFactory;
        _paths = paths;
        _keyStore = keyStore;
        _deviceSigner = deviceSigner;
        _bootstrap = bootstrap;
    }

    public async Task<AccountResetInitiateResponseDto> SubmitResetRequestAsync(
        string directorEndpoint,
        string? requestedNewUsername,
        string reason,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(directorEndpoint))
        {
            throw new ArgumentException("Vui lòng chọn máy Giám đốc đang hoạt động trên LAN.", nameof(directorEndpoint));
        }

        CompanyProfileEntity? company = await _bootstrap.GetCompanyProfileAsync(ct).ConfigureAwait(false);
        LocalUserEntity? localUser = await _bootstrap.GetPrimaryLocalUserAsync(ct).ConfigureAwait(false);
        if (company is null || !company.IsGenesisCompleted || localUser is null)
        {
            throw new InvalidOperationException("Máy trạm chưa được ghép đôi hoặc chưa có tài khoản cục bộ.");
        }

        byte[] devicePriv = _keyStore.LoadDevicePrivateKey()
            ?? throw new InvalidOperationException("Không tìm thấy khóa riêng thiết bị.");
        byte[] devicePub = _keyStore.LoadDevicePublicKey()
            ?? throw new InvalidOperationException("Không tìm thấy khóa công khai thiết bị.");

        try
        {
            var dto = new AccountResetInitiateRequestDto(
                _paths.MachineId,
                localUser.Username,
                localUser.FullName,
                string.IsNullOrWhiteSpace(requestedNewUsername) ? null : requestedNewUsername.Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(reason) ? "Quên mật khẩu đăng nhập cục bộ" : reason.Trim());

            string baseUrl = NormalizeBaseUrl(directorEndpoint);
            byte[] bodyBytes = JsonSerializer.SerializeToUtf8Bytes(dto);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/account-recovery/request")
            {
                Content = new ByteArrayContent(bodyBytes)
            };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            _deviceSigner.SignHttpRequest(req, bodyBytes, devicePriv, devicePub, company.CompanyId, _paths.MachineId);

            using HttpClient client = _httpFactory.CreateClient("EzLan");
            using HttpResponseMessage resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Máy Giám đốc từ chối yêu cầu ({(int)resp.StatusCode}): {err}");
            }

            return await resp.Content.ReadFromJsonAsync<AccountResetInitiateResponseDto>(cancellationToken: ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Phản hồi từ máy Giám đốc rỗng.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(devicePriv);
        }
    }

    public async Task<AccountResetStatusResponseDto> PollStatusOnceAsync(
        string directorEndpoint,
        Guid requestId,
        CancellationToken ct = default)
    {
        CompanyProfileEntity? company = await _bootstrap.GetCompanyProfileAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Máy trạm chưa được ghép đôi.");

        byte[] devicePriv = _keyStore.LoadDevicePrivateKey()
            ?? throw new InvalidOperationException("Không tìm thấy khóa riêng thiết bị.");
        byte[] devicePub = _keyStore.LoadDevicePublicKey()
            ?? throw new InvalidOperationException("Không tìm thấy khóa công khai thiết bị.");

        try
        {
            string baseUrl = NormalizeBaseUrl(directorEndpoint);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/account-recovery/status/{requestId}");
            _deviceSigner.SignHttpRequest(req, null, devicePriv, devicePub, company.CompanyId, _paths.MachineId);

            using HttpClient client = _httpFactory.CreateClient("EzLan");
            using HttpResponseMessage resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            return await resp.Content.ReadFromJsonAsync<AccountResetStatusResponseDto>(cancellationToken: ct).ConfigureAwait(false)
                ?? new AccountResetStatusResponseDto(requestId, "Pending", "Đang chờ Giám đốc duyệt...", null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(devicePriv);
        }
    }

    public async Task<(AccountResetStatusResponseDto FinalStatus, LocalUserEntity? UpdatedUser)> WaitAndApplyResetAsync(
        string directorEndpoint,
        Guid requestId,
        string newLocalPassword,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromMinutes(10));
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            AccountResetStatusResponseDto status = await PollStatusOnceAsync(directorEndpoint, requestId, ct).ConfigureAwait(false);
            if (string.Equals(status.State, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(status.SignedResetTicketJson))
                {
                    throw new InvalidOperationException("Giám đốc đã duyệt nhưng thiếu vé xác nhận ký số.");
                }

                LocalUserEntity updated = await _bootstrap
                    .ApplyApprovedPasswordResetAsync(status.SignedResetTicketJson, newLocalPassword, ct)
                    .ConfigureAwait(false);
                return (status, updated);
            }

            if (string.Equals(status.State, "Rejected", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status.State, "Expired", StringComparison.OrdinalIgnoreCase))
            {
                return (status, null);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }

        return (new AccountResetStatusResponseDto(requestId, "Expired", "Hết thời gian chờ Giám đốc duyệt.", null), null);
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
