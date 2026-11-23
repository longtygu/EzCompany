using System.Security.Cryptography;
using System.Text;
using EzCompany.Core.Crypto;

namespace EzCompany.Core.LanAgent.Security;

/// <summary>
/// Signs and constructs canonical administrative LAN requests using the Director's Ed25519 Root Private Key.
/// </summary>
public sealed class DirectorRequestSigner
{
    public const string TimestampHeader = "X-Ez-Timestamp";
    public const string NonceHeader = "X-Ez-Nonce";
    public const string SignatureHeader = "X-Ez-Signature";
    public const string AdminNameHeader = "X-Ez-Admin-Name";

    private readonly Ed25519Service _ed25519;

    public DirectorRequestSigner(Ed25519Service ed25519)
    {
        _ed25519 = ed25519;
    }

    public static string BuildCanonicalString(
        string method,
        string pathAndQuery,
        string timestampUnixSeconds,
        string nonce,
        ReadOnlySpan<byte> bodyBytes)
    {
        string bodyHashHex = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();
        return $"{method.ToUpperInvariant()}|{pathAndQuery}|{timestampUnixSeconds}|{nonce}|{bodyHashHex}";
    }

    public void SignHttpRequest(
        HttpRequestMessage request,
        byte[]? bodyBytes,
        byte[] rootPrivateKey32,
        string adminName,
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
        byte[] body = bodyBytes ?? [];

        string canonical = BuildCanonicalString(request.Method.Method, pathAndQuery, ts, nonce, body);
        byte[] sig = _ed25519.Sign(rootPrivateKey32, Encoding.UTF8.GetBytes(canonical));

        request.Headers.Remove(TimestampHeader);
        request.Headers.Remove(NonceHeader);
        request.Headers.Remove(SignatureHeader);
        request.Headers.Remove(AdminNameHeader);

        request.Headers.TryAddWithoutValidation(TimestampHeader, ts);
        request.Headers.TryAddWithoutValidation(NonceHeader, nonce);
        request.Headers.TryAddWithoutValidation(SignatureHeader, Convert.ToBase64String(sig));
        request.Headers.TryAddWithoutValidation(AdminNameHeader, string.IsNullOrWhiteSpace(adminName) ? "director" : adminName);
    }
}
