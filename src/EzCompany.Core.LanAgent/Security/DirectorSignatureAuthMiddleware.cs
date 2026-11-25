using System.Globalization;
using System.Text;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Crypto;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.LanAgent.Security;

/// <summary>
/// Enforces Ed25519 Director signature authentication on administrative LAN APIs and
/// Ed25519 Device signature authentication on peer-to-peer task and account recovery APIs,
/// with ±60s timestamp tolerance and nonce replay protection.
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
        IDbContextFactory<AppDbContext> appDbFactory,
        TimeProvider timeProvider)
    {
        string path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/pairing/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (IsDeviceSignedRoute(path))
        {
            await HandleDeviceSignedRequestAsync(context, ed25519, replayCache, audit, appDbFactory, timeProvider)
                .ConfigureAwait(false);
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

        byte[] bodyBytes = await ReadRequestBodyAsync(context).ConfigureAwait(false);
        string pathAndQuery = $"{context.Request.Path.Value}{context.Request.QueryString.Value}";
        string canonical = DirectorRequestSigner.BuildCanonicalString(
            context.Request.Method,
            pathAndQuery,
            tsRaw,
            nonce,
            bodyBytes);

        if (!ed25519.Verify(pinnedRootPub, Encoding.UTF8.GetBytes(canonical), signatureBytes))
        {
            await RejectAsync(context, audit, "INVALID_SIGNATURE", $"Chữ ký Ed25519 không hợp lệ cho {pathAndQuery}.").ConfigureAwait(false);
            return;
        }

        context.Items["EzAdminName"] = adminName;
        await _next(context).ConfigureAwait(false);
    }

    private async Task HandleDeviceSignedRequestAsync(
        HttpContext context,
        Ed25519Service ed25519,
        NonceReplayCache replayCache,
        IAuditLogger audit,
        IDbContextFactory<AppDbContext> appDbFactory,
        TimeProvider timeProvider)
    {
        if (!context.Request.Headers.TryGetValue(DeviceRequestSigner.MachineIdHeader, out var machineValues) ||
            !context.Request.Headers.TryGetValue(DeviceRequestSigner.DevicePubKeyHeader, out var pubKeyValues) ||
            !context.Request.Headers.TryGetValue(DeviceRequestSigner.CompanyIdHeader, out var companyValues) ||
            !context.Request.Headers.TryGetValue(DeviceRequestSigner.TimestampHeader, out var tsValues) ||
            !context.Request.Headers.TryGetValue(DeviceRequestSigner.NonceHeader, out var nonceValues) ||
            !context.Request.Headers.TryGetValue(DeviceRequestSigner.SignatureHeader, out var sigValues))
        {
            await RejectAsync(context, audit, "MISSING_DEVICE_AUTH_HEADERS", "Thiếu header xác thực chữ ký thiết bị.").ConfigureAwait(false);
            return;
        }

        string machineId = machineValues.ToString().Trim();
        string devicePubB64 = pubKeyValues.ToString().Trim();
        string companyIdRaw = companyValues.ToString().Trim();
        string tsRaw = tsValues.ToString().Trim();
        string nonce = nonceValues.ToString().Trim();
        string sigB64 = sigValues.ToString().Trim();

        if (string.IsNullOrWhiteSpace(machineId) ||
            !Guid.TryParse(companyIdRaw, out Guid reqCompanyId) ||
            !long.TryParse(tsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixSec))
        {
            await RejectAsync(context, audit, "MALFORMED_DEVICE_HEADER", "Header thiết bị hoặc công ty không hợp lệ.").ConfigureAwait(false);
            return;
        }

        DateTimeOffset requestTimeUtc = DateTimeOffset.FromUnixTimeSeconds(unixSec);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        if ((nowUtc - requestTimeUtc).Duration() > MaxClockSkew)
        {
            await RejectAsync(context, audit, "TIMESTAMP_OUT_OF_WINDOW", "Yêu cầu vượt quá sai lệch thời gian ±60 giây.").ConfigureAwait(false);
            return;
        }

        if (!replayCache.TryAccept($"dev:{nonce}", nowUtc))
        {
            await RejectAsync(context, audit, "NONCE_REPLAY", "Phát hiện lặp lại X-Ez-Nonce (Replay Attack).").ConfigureAwait(false);
            return;
        }

        byte[] devicePubBytes;
        byte[] sigBytes;
        try
        {
            devicePubBytes = Convert.FromBase64String(devicePubB64);
            sigBytes = Convert.FromBase64String(sigB64);
        }
        catch (FormatException)
        {
            await RejectAsync(context, audit, "INVALID_DEVICE_KEY_FORMAT", "Khóa thiết bị hoặc chữ ký không hợp lệ.").ConfigureAwait(false);
            return;
        }

        if (devicePubBytes.Length != Ed25519Service.KeySizeBytes)
        {
            await RejectAsync(context, audit, "INVALID_DEVICE_KEY_LENGTH", "Độ dài khóa thiết bị không hợp lệ.").ConfigureAwait(false);
            return;
        }

        await using AppDbContext db = await appDbFactory.CreateDbContextAsync(context.RequestAborted).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(context.RequestAborted)
            .ConfigureAwait(false);
        if (company is null || !company.IsGenesisCompleted || company.CompanyId != reqCompanyId)
        {
            await RejectAsync(context, audit, "COMPANY_MISMATCH", "Mã công ty không khớp với máy nhận.").ConfigureAwait(false);
            return;
        }

        string path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/v1/account-recovery/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/peers/", StringComparison.OrdinalIgnoreCase))
        {
            PairedDeviceEntity? registered = await db.PairedDevices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.MachineId == machineId, context.RequestAborted)
                .ConfigureAwait(false);

            if (registered is null ||
                !string.Equals(registered.DevicePublicKeyBase64, devicePubB64, StringComparison.Ordinal))
            {
                await RejectAsync(context, audit, "UNREGISTERED_DEVICE", "Thiết bị chưa được ghép đôi hoặc khóa thiết bị không khớp.").ConfigureAwait(false);
                return;
            }
        }

        byte[] bodyBytes = await ReadRequestBodyAsync(context).ConfigureAwait(false);
        string pathAndQuery = $"{context.Request.Path.Value}{context.Request.QueryString.Value}";
        string canonical = DeviceRequestSigner.BuildCanonicalString(
            context.Request.Method,
            pathAndQuery,
            tsRaw,
            nonce,
            companyIdRaw,
            machineId,
            bodyBytes);

        if (!ed25519.Verify(devicePubBytes, Encoding.UTF8.GetBytes(canonical), sigBytes))
        {
            await RejectAsync(context, audit, "INVALID_DEVICE_SIGNATURE", $"Chữ ký thiết bị Ed25519 không hợp lệ cho {pathAndQuery}.").ConfigureAwait(false);
            return;
        }

        context.Items["EzDeviceMachineId"] = machineId;
        context.Items["EzDevicePubKey"] = devicePubB64;
        await _next(context).ConfigureAwait(false);
    }

    private static bool IsDeviceSignedRoute(string path) =>
        path.StartsWith("/api/v1/account-recovery/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/v1/tasks/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/v1/peers/", StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadRequestBodyAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms, context.RequestAborted).ConfigureAwait(false);
        context.Request.Body.Position = 0;
        return ms.ToArray();
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
