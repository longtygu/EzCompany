using System.Text;
using System.Text.Json;
using EzCompany.Core.Abstractions;

namespace EzCompany.Core.Crypto;

/// <summary>
/// Issues and strictly verifies signed <c>policy.lic</c> documents offline.
/// </summary>
public sealed class PolicyEngine
{
    public const int DefaultValidityDays = 14;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly Ed25519Service _ed25519;
    private readonly ClockGuardService _clockGuard;

    public PolicyEngine(Ed25519Service ed25519, ClockGuardService clockGuard)
    {
        _ed25519 = ed25519;
        _clockGuard = clockGuard;
    }

    public SignedPolicyDocument IssueSignedPolicy(PolicyPayload payload, byte[] rootPrivateKey32)
    {
        ArgumentNullException.ThrowIfNull(payload);
        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        byte[] signature = _ed25519.Sign(rootPrivateKey32, payloadBytes);
        return new SignedPolicyDocument(
            Convert.ToBase64String(payloadBytes),
            Convert.ToBase64String(signature));
    }

    public string SerializeDocument(SignedPolicyDocument document) =>
        JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });

    public SignedPolicyDocument? DeserializeDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SignedPolicyDocument>(json);
        }
        catch
        {
            return null;
        }
    }

    public PolicyPayload? TryDecodePayloadUnverified(SignedPolicyDocument document)
    {
        try
        {
            byte[] payloadBytes = Convert.FromBase64String(document.PayloadBase64);
            return JsonSerializer.Deserialize<PolicyPayload>(payloadBytes);
        }
        catch
        {
            return null;
        }
    }

    public PolicyVerificationResult VerifyOffline(
        string? signedDocumentJson,
        byte[]? pinnedRootPublicKey32,
        string expectedMachineId,
        byte[]? expectedDevicePublicKey32,
        bool commitClockGuard = true)
    {
        if (string.IsNullOrWhiteSpace(signedDocumentJson))
        {
            return Fail(PolicyFailureReason.MissingLicense, "Chưa có tệp giấy phép policy.lic.");
        }

        if (pinnedRootPublicKey32 is null || pinnedRootPublicKey32.Length != Ed25519Service.KeySizeBytes)
        {
            return Fail(PolicyFailureReason.MissingPinnedKey, "Chưa ghim khóa công khai Root của công ty.");
        }

        SignedPolicyDocument? doc = DeserializeDocument(signedDocumentJson);
        if (doc is null || string.IsNullOrWhiteSpace(doc.PayloadBase64) || string.IsNullOrWhiteSpace(doc.SignatureBase64))
        {
            return Fail(PolicyFailureReason.MalformedDocument, "Định dạng policy.lic không hợp lệ.");
        }

        byte[] payloadBytes;
        byte[] signatureBytes;
        PolicyPayload? payload;
        try
        {
            payloadBytes = Convert.FromBase64String(doc.PayloadBase64);
            signatureBytes = Convert.FromBase64String(doc.SignatureBase64);
            payload = JsonSerializer.Deserialize<PolicyPayload>(payloadBytes);
        }
        catch
        {
            return Fail(PolicyFailureReason.MalformedDocument, "Không thể giải mã nội dung policy.lic.");
        }

        if (payload is null)
        {
            return Fail(PolicyFailureReason.MalformedDocument, "Payload của policy.lic rỗng.");
        }

        if (!_ed25519.Verify(pinnedRootPublicKey32, payloadBytes, signatureBytes))
        {
            return Fail(PolicyFailureReason.InvalidSignature, "Chữ ký Ed25519 của policy.lic không khớp khóa gốc công ty.", payload);
        }

        if (!string.Equals(payload.MachineId, expectedMachineId, StringComparison.Ordinal))
        {
            return Fail(PolicyFailureReason.MachineIdMismatch, "policy.lic không thuộc MachineId của máy trạm này.", payload);
        }

        string expectedDevicePubB64 = expectedDevicePublicKey32 is null
            ? string.Empty
            : Convert.ToBase64String(expectedDevicePublicKey32);

        if (string.IsNullOrEmpty(expectedDevicePubB64) ||
            !string.Equals(payload.DevicePublicKey, expectedDevicePubB64, StringComparison.Ordinal))
        {
            return Fail(PolicyFailureReason.DevicePublicKeyMismatch, "Khóa thiết bị trong policy.lic không khớp.", payload);
        }

        if (_clockGuard.IsClockRollbackDetected(out ClockGuardState? guardState))
        {
            return Fail(
                PolicyFailureReason.ClockRollbackDetected,
                "Phát hiện đồng hồ hệ thống bị tua ngược quá 5 phút so với lần chạy trước.",
                payload);
        }

        if (guardState is not null && payload.PolicyVersion < guardState.LastAcceptedPolicyVersion)
        {
            return Fail(
                PolicyFailureReason.PolicyVersionDowngrade,
                $"Từ chối hạ phiên bản policy ({payload.PolicyVersion} < {guardState.LastAcceptedPolicyVersion}).",
                payload);
        }

        DateTimeOffset nowUtc = _clockGuard.GetCurrentUtc();
        if (nowUtc > payload.ExpiresAtUtc ||
            (guardState is not null && guardState.LastRunTimestampUtc > payload.ExpiresAtUtc))
        {
            return Fail(
                PolicyFailureReason.PolicyExpired,
                $"Giấy phép policy.lic đã hết hạn vào {payload.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC. Vui lòng đồng bộ LAN với máy Giám đốc.",
                payload);
        }

        if (commitClockGuard)
        {
            _clockGuard.RecordVerifiedRun(payload.PolicyVersion, nowUtc);
        }

        var snapshot = ToSnapshot(payload);
        return new PolicyVerificationResult(true, PolicyFailureReason.None, "Hợp lệ", payload, snapshot);
    }

    public static PolicySnapshot ToSnapshot(PolicyPayload payload, bool isLocked = false, string? lockReason = null) =>
        new(
            payload.CompanyId,
            payload.MachineId,
            payload.DevicePublicKey,
            payload.CompanyEncryptionPublicKey,
            payload.UserId,
            payload.Username,
            payload.FullName,
            payload.Role,
            payload.EnabledModules,
            payload.EnabledPermissions,
            payload.PolicyVersion,
            payload.IssuedAtUtc,
            payload.ExpiresAtUtc,
            payload.AllowedUpdateChannel,
            payload.MaxApprovedVersion,
            payload.IssuedByAdminName,
            isLocked,
            lockReason);

    private static PolicyVerificationResult Fail(PolicyFailureReason reason, string message, PolicyPayload? payload = null)
    {
        PolicySnapshot? lockedSnapshot = payload is null ? null : ToSnapshot(payload, isLocked: true, lockReason: message);
        return new PolicyVerificationResult(false, reason, message, payload, lockedSnapshot);
    }
}
