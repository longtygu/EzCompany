using System.Globalization;
using System.Text;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using Microsoft.AspNetCore.Http;

namespace EzCompany.Core.LanAgent.Security;

/// <summary>
/// Enforces Ed25519 Director signature authentication, ±60s timestamp tolerance,
/// and 2-minute nonce replay protection on all administrative LAN API endpoints.
/// </summary>
public sealed class DirectorSignatureAuthMiddleware
{
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(60);

    private readonly RequestDelegate _next;

    public DirectorSignatureAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        LocalKeyStore keyStore,
        Ed25519Service ed25519,
        NonceReplayCache replayCache,
        IAuditLogger audit,
        TimeProvider timeProvider)
    {
        string path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/pairing/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        byte[]? pinnedRootPub = keyStore.LoadPinnedRootPublicKey();
        if (pinnedRootPub is null || pinnedRootPub.Length != Ed25519Service.KeySizeBytes)
        {
            await RejectAsync(context, audit, "MISSING_ROOT_KEY", "Máy trạm chưa ghim khóa gốc công ty.").ConfigureAwait(false);
            return;
        }

        if (!context.Request.Headers.TryGetValue(DirectorRequestSigner.TimestampHeader, out var tsValues) ||
            !context.Request.Headers.TryGetValue(DirectorRequestSigner.NonceHeader, out var nonceValues) ||
            !context.Request.Headers.TryGetValue(DirectorRequestSigner.SignatureHeader, out var sigValues) ||
            !context.Request.Headers.TryGetValue(DirectorRequestSigner.AdminNameHeader, out var adminValues))
        {
            await RejectAsync(context, audit, "MISSING_AUTH_HEADERS", "Thiếu header xác thực chữ ký Giám đốc.").ConfigureAwait(false);
            return;
        }

        string tsRaw = tsValues.ToString();
        string nonce = nonceValues.ToString();
        string sigB64 = sigValues.ToString();
        string adminName = adminValues.ToString();

        if (string.IsNullOrWhiteSpace(adminName) ||
            !long.TryParse(tsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixSec))
        {
            await RejectAsync(context, audit, "MALFORMED_AUTH_HEADER", "Header thời gian hoặc tên quản trị không hợp lệ.").ConfigureAwait(false);
            return;
        }

        DateTimeOffset requestTimeUtc = DateTimeOffset.FromUnixTimeSeconds(unixSec);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        if ((nowUtc - requestTimeUtc).Duration() > MaxClockSkew)
        {
            await RejectAsync(context, audit, "TIMESTAMP_OUT_OF_WINDOW", "Yêu cầu vượt quá sai lệch thời gian ±60 giây.").ConfigureAwait(false);
            return;
        }

        if (!replayCache.TryAccept(nonce, nowUtc))
        {
            await RejectAsync(context, audit, "NONCE_REPLAY", "Phát hiện lặp lại X-Ez-Nonce (Replay Attack).").ConfigureAwait(false);
            return;
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(sigB64);
        }
        catch (FormatException)
        {
            await RejectAsync(context, audit, "INVALID_SIGNATURE_FORMAT", "Định dạng chữ ký không hợp lệ.").ConfigureAwait(false);
            return;
        }

        context.Request.EnableBuffering();
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms, context.RequestAborted).ConfigureAwait(false);
        context.Request.Body.Position = 0;

        string pathAndQuery = $"{context.Request.Path.Value}{context.Request.QueryString.Value}";
        string canonical = DirectorRequestSigner.BuildCanonicalString(
            context.Request.Method,
            pathAndQuery,
            tsRaw,
            nonce,
            ms.ToArray());

        if (!ed25519.Verify(pinnedRootPub, Encoding.UTF8.GetBytes(canonical), signatureBytes))
        {
            await RejectAsync(context, audit, "INVALID_SIGNATURE", $"Chữ ký Ed25519 không hợp lệ cho {pathAndQuery}.").ConfigureAwait(false);
            return;
        }

        context.Items["EzAdminName"] = adminName;
        await _next(context).ConfigureAwait(false);
    }

    private static async Task RejectAsync(HttpContext context, IAuditLogger audit, string code, string reason)
    {
        try
        {
            await audit.LogAsync(
                "LAN_AUTH_REJECTED",
                "core.lan",
                context.Request.Path.Value,
                $"{code}: {reason}",
                "Warning",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = code, message = reason }).ConfigureAwait(false);
    }
}
