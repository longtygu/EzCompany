using System.Net.Http.Json;
using System.Security.Cryptography;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;

namespace EzCompany.Core.LanAgent.Pairing;

public sealed record StartedPairingSession(
    Guid RequestId,
    string SasCode,
    string ClientNonceBase64,
    PairingInitiateResponse DirectorMetadata);

/// <summary>
/// Employee-side LAN pairing client: creates local Device Ed25519 keys, computes SAS 6-digit locally,
/// polls Director approval without sending any raw/hashed password across the LAN, and completes offline onboarding.
/// </summary>
public sealed class LanPairingClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly Ed25519Service _ed25519;
    private readonly LocalKeyStore _keyStore;
    private readonly GenesisBootstrapService _bootstrap;

    public LanPairingClient(
        IHttpClientFactory httpFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        Ed25519Service ed25519,
        LocalKeyStore keyStore,
        GenesisBootstrapService bootstrap)
    {
        _httpFactory = httpFactory;
        _paths = paths;
        _session = session;
        _ed25519 = ed25519;
        _keyStore = keyStore;
        _bootstrap = bootstrap;
    }

    public async Task<StartedPairingSession> InitiatePairingAsync(
        string directorEndpoint,
        string fullName,
        string username,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(directorEndpoint))
        {
            throw new ArgumentException("Chưa chọn máy Giám đốc.", nameof(directorEndpoint));
        }

        string baseUrl = NormalizeBaseUrl(directorEndpoint);
        (byte[] devicePriv, byte[] devicePub) = EnsureLocalDeviceKeyPair();
        try
        {
            byte[] clientNonce = RandomNumberGenerator.GetBytes(32);
            string clientNonceB64 = Convert.ToBase64String(clientNonce);

            var payload = new PairingInitiateRequest(
                _paths.MachineId,
                Convert.ToBase64String(devicePub),
                fullName.Trim(),
                username.Trim().ToLowerInvariant(),
                clientNonceB64,
                _session.CurrentAppVersion,
                _session.CurrentLanPort);

            using HttpClient client = _httpFactory.CreateClient("EzLan");
            using HttpResponseMessage resp = await client
                .PostAsJsonAsync($"{baseUrl}/api/v1/pairing/initiate", payload, ct)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                string errText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Máy Giám đốc từ chối kết nối ({(int)resp.StatusCode}): {errText}");
            }

            PairingInitiateResponse initResp = await resp.Content
                .ReadFromJsonAsync<PairingInitiateResponse>(cancellationToken: ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Phản hồi ghép đôi từ Giám đốc rỗng.");

            byte[] rootPub = Convert.FromBase64String(initResp.RootPublicKeyBase64);
            byte[] serverNonce = Convert.FromBase64String(initResp.ServerNonceBase64);
            string localSas = SasCalculator.ComputeSixDigitCode(rootPub, devicePub, clientNonce, serverNonce);

            return new StartedPairingSession(initResp.RequestId, localSas, clientNonceB64, initResp);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(devicePriv);
        }
    }

    public async Task<PairingStatusResponse> PollOnceAsync(
        string directorEndpoint,
        Guid requestId,
        string clientNonceBase64,
        CancellationToken ct = default)
    {
        string baseUrl = NormalizeBaseUrl(directorEndpoint);
        using HttpClient client = _httpFactory.CreateClient("EzLan");
        string url =
            $"{baseUrl}/api/v1/pairing/status/{requestId}?machineId={Uri.EscapeDataString(_paths.MachineId)}&clientNonce={Uri.EscapeDataString(clientNonceBase64)}";

        PairingStatusResponse? status = await client
            .GetFromJsonAsync<PairingStatusResponse>(url, ct)
            .ConfigureAwait(false);

        return status ?? new PairingStatusResponse(requestId, PairingRequestState.Pending, "Đang chờ...", null, null);
    }

    public async Task<PairingStatusResponse> WaitAndCompleteOnboardingAsync(
        string directorEndpoint,
        StartedPairingSession started,
        string fullName,
        string username,
        string localPassword,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            PairingStatusResponse status = await PollOnceAsync(
                directorEndpoint,
                started.RequestId,
                started.ClientNonceBase64,
                ct).ConfigureAwait(false);

            if (status.State == PairingRequestState.Approved)
            {
                if (string.IsNullOrWhiteSpace(status.SignedPolicyJson) || status.UserId is null)
                {
                    throw new InvalidOperationException("Dữ liệu policy trả về không đầy đủ.");
                }

                await _bootstrap.CompleteEmployeeOnboardingAsync(
                    started.DirectorMetadata.CompanyId,
                    started.DirectorMetadata.CompanyName,
                    started.DirectorMetadata.TaxCode,
                    Convert.FromBase64String(started.DirectorMetadata.RootPublicKeyBase64),
                    Convert.FromBase64String(started.DirectorMetadata.CompanyEncryptionPublicKeyBase64),
                    status.UserId.Value,
                    username,
                    fullName,
                    localPassword,
                    status.SignedPolicyJson,
                    ct).ConfigureAwait(false);

                return status;
            }

            if (status.State is PairingRequestState.Rejected or PairingRequestState.Expired)
            {
                return status;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }

        return new PairingStatusResponse(started.RequestId, PairingRequestState.Expired, "Hết thời gian chờ duyệt.", null, null);
    }

    private (byte[] PrivateKey, byte[] PublicKey) EnsureLocalDeviceKeyPair()
    {
        byte[]? existingPriv = _keyStore.LoadDevicePrivateKey();
        byte[]? existingPub = _keyStore.LoadDevicePublicKey();
        if (existingPriv is not null && existingPub is not null)
        {
            return (existingPriv, existingPub);
        }

        Ed25519KeyPair generated = _ed25519.GenerateKeyPair();
        _keyStore.SaveDeviceKeyPair(generated.PrivateKey, generated.PublicKey);
        return (generated.PrivateKey, generated.PublicKey);
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
