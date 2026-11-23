using EzCompany.Core.Abstractions;

namespace EzCompany.Core.Crypto;

public sealed record PolicyPayload(
    Guid CompanyId,
    string MachineId,
    string DevicePublicKey,
    string CompanyEncryptionPublicKey,
    Guid UserId,
    string Username,
    string FullName,
    string Role,
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<string> EnabledPermissions,
    long PolicyVersion,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string AllowedUpdateChannel,
    string MaxApprovedVersion,
    string IssuedByAdminName);

public sealed record SignedPolicyDocument(
    string PayloadBase64,
    string SignatureBase64);

public sealed record ClockGuardState(
    DateTimeOffset LastRunTimestampUtc,
    long LastAcceptedPolicyVersion);

public enum PolicyFailureReason
{
    None = 0,
    MissingLicense = 1,
    MissingPinnedKey = 2,
    MalformedDocument = 3,
    InvalidSignature = 4,
    MachineIdMismatch = 5,
    DevicePublicKeyMismatch = 6,
    PolicyVersionDowngrade = 7,
    ClockRollbackDetected = 8,
    PolicyExpired = 9
}

public sealed record PolicyVerificationResult(
    bool IsValid,
    PolicyFailureReason FailureReason,
    string Message,
    PolicyPayload? Payload,
    PolicySnapshot? Snapshot);
