using System.Security.Cryptography;
using System.Text;
using EzCompany.Core.Crypto;

namespace EzCompany.Core.LanAgent.Security;

/// <summary>
/// Signs and builds canonical peer-to-peer LAN requests using a workstation's own Ed25519 Device Private Key.
/// Used for colleague-to-colleague task assignment/handover and employee password reset requests.
/// </summary>
public sealed class DeviceRequestSigner
{
    public const string MachineIdHeader = "X-Ez-Device-MachineId";
    public const string DevicePubKeyHeader = "X-Ez-Device-PubKey";
    public const string CompanyIdHeader = "X-Ez-Company-Id";
    public const string TimestampHeader = "X-Ez-Timestamp";
    public const string NonceHeader = "X-Ez-Nonce";
    public const string SignatureHeader = "X-Ez-Signature";

    private readonly Ed25519Service _ed25519;

    public DeviceRequestSigner(Ed25519Service ed25519)
    {
        _ed25519 = ed25519;
    }

    public static string BuildCanonicalString(
        string method,
        string pathAndQuery,
        string timestampUnixSeconds,
        string nonce,
        string companyId,
        string machineId,
        ReadOnlySpan<byte> bodyBytes)
    {
        string bodyHashHex = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();
        return $"DEVICE|{method.ToUpperInvariant()}|{pathAndQuery}|{timestampUnixSeconds}|{nonce}|{companyId}|{machineId}|{bodyHashHex}";
    }

    public void SignHttpRequest(
        HttpRequestMessage request,
        byte[]? bodyBytes,
        byte[] devicePrivateKey32,
        byte[] devicePublicKey32,
        Guid companyId,
        string machineId,
        DateTimeOffset? overrideUtc = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is null)
        {
            throw new ArgumentException("RequestUri must be set before signing.", nameof(request));
        }

        string pathAndQuery = request.RequestUri.IsAbsoluteUri
            ? request.RequestUri.PathAndQuery
            : request.RequestUri.ToString();

        string ts = (overrideUtc ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        string nonce = Guid.NewGuid().ToString("N");
        string companyIdStr = companyId.ToString("D");
        string devicePubB64 = Convert.ToBase64String(devicePublicKey32);
        byte[] body = bodyBytes ?? [];

        string canonical = BuildCanonicalString(
            request.Method.Method,
            pathAndQuery,
            ts,
            nonce,
            companyIdStr,
            machineId,
            body);

        byte[] sig = _ed25519.Sign(devicePrivateKey32, Encoding.UTF8.GetBytes(canonical));

        request.Headers.Remove(MachineIdHeader);
        request.Headers.Remove(DevicePubKeyHeader);
        request.Headers.Remove(CompanyIdHeader);
        request.Headers.Remove(TimestampHeader);
        request.Headers.Remove(NonceHeader);
        request.Headers.Remove(SignatureHeader);

        request.Headers.TryAddWithoutValidation(MachineIdHeader, machineId);
        request.Headers.TryAddWithoutValidation(DevicePubKeyHeader, devicePubB64);
        request.Headers.TryAddWithoutValidation(CompanyIdHeader, companyIdStr);
        request.Headers.TryAddWithoutValidation(TimestampHeader, ts);
        request.Headers.TryAddWithoutValidation(NonceHeader, nonce);
        request.Headers.TryAddWithoutValidation(SignatureHeader, Convert.ToBase64String(sig));
    }
}
