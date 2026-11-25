using System.Text;
using System.Text.Json;

namespace EzCompany.Core.Crypto;

public sealed record PasswordResetTicketPayload(
    Guid RequestId,
    Guid CompanyId,
    string MachineId,
    string DevicePublicKeyBase64,
    string? ApprovedUsername,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string ApprovedByAdminName);

public sealed record SignedPasswordResetTicket(
    string PayloadJson,
    string SignatureBase64);

/// <summary>
/// Issues and verifies Ed25519 Root-signed authorization tickets allowing an employee workstation
/// to reset its local Argon2id login password offline without transmitting any password over LAN.
/// </summary>
public sealed class PasswordResetTicketService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly Ed25519Service _ed25519;
    private readonly TimeProvider _timeProvider;

    public PasswordResetTicketService(Ed25519Service ed25519, TimeProvider? timeProvider = null)
    {
        _ed25519 = ed25519;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string IssueSignedTicketJson(PasswordResetTicketPayload payload, byte[] rootPrivateKey32)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        byte[] sig = _ed25519.Sign(rootPrivateKey32, Encoding.UTF8.GetBytes(payloadJson));
        var doc = new SignedPasswordResetTicket(payloadJson, Convert.ToBase64String(sig));
        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    public (bool IsValid, string Message, PasswordResetTicketPayload? Payload) VerifyTicketJson(
        string? signedTicketJson,
        byte[]? pinnedRootPublicKey32,
        string expectedMachineId,
        byte[]? expectedDevicePublicKey32,
        Guid expectedCompanyId)
    {
        if (string.IsNullOrWhiteSpace(signedTicketJson))
        {
            return (false, "Thiếu vé xác nhận cấp lại mật khẩu.", null);
        }

        if (pinnedRootPublicKey32 is null || pinnedRootPublicKey32.Length != Ed25519Service.KeySizeBytes)
        {
            return (false, "Chưa có khóa công khai gốc (RootPublicKey) trên máy.", null);
        }

        if (expectedDevicePublicKey32 is null || expectedDevicePublicKey32.Length != Ed25519Service.KeySizeBytes)
        {
            return (false, "Chưa có khóa công khai thiết bị trên máy.", null);
        }

        SignedPasswordResetTicket? doc;
        try
        {
            doc = JsonSerializer.Deserialize<SignedPasswordResetTicket>(signedTicketJson, JsonOptions);
        }
        catch (JsonException)
        {
            return (false, "Định dạng vé cấp lại mật khẩu không hợp lệ.", null);
        }

        if (doc is null || string.IsNullOrWhiteSpace(doc.PayloadJson) || string.IsNullOrWhiteSpace(doc.SignatureBase64))
        {
            return (false, "Dữ liệu vé cấp lại mật khẩu bị thiếu.", null);
        }

        byte[] sigBytes;
        try
        {
            sigBytes = Convert.FromBase64String(doc.SignatureBase64);
        }
        catch (FormatException)
        {
            return (false, "Chữ ký vé cấp lại mật khẩu không đúng định dạng Base64.", null);
        }

        if (!_ed25519.Verify(pinnedRootPublicKey32, Encoding.UTF8.GetBytes(doc.PayloadJson), sigBytes))
        {
            return (false, "Chữ ký Ed25519 của Giám đốc trên vé cấp lại mật khẩu không hợp lệ.", null);
        }

        PasswordResetTicketPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PasswordResetTicketPayload>(doc.PayloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            return (false, "Nội dung vé cấp lại mật khẩu không hợp lệ.", null);
        }

        if (payload is null)
        {
            return (false, "Nội dung vé cấp lại mật khẩu rỗng.", null);
        }

        if (payload.CompanyId != expectedCompanyId)
        {
            return (false, "Vé cấp lại mật khẩu không thuộc công ty hiện tại.", payload);
        }

        if (!string.Equals(payload.MachineId, expectedMachineId, StringComparison.Ordinal))
        {
            return (false, "Vé cấp lại mật khẩu không khớp MachineId của máy này.", payload);
        }

        string expectedDevPubB64 = Convert.ToBase64String(expectedDevicePublicKey32);
        if (!string.Equals(payload.DevicePublicKeyBase64, expectedDevPubB64, StringComparison.Ordinal))
        {
            return (false, "Vé cấp lại mật khẩu không khớp khóa thiết bị hiện tại.", payload);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (now > payload.ExpiresAtUtc)
        {
            return (false, "Vé cấp lại mật khẩu đã hết hạn.", payload);
        }

        return (true, "Vé cấp lại mật khẩu hợp lệ.", payload);
    }
}
