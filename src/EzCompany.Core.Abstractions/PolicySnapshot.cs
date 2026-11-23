namespace EzCompany.Core.Abstractions;

/// <summary>
/// Verified runtime snapshot of the workstation's signed <c>policy.lic</c>.
/// </summary>
public sealed record PolicySnapshot(
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
    string IssuedByAdminName,
    bool IsLocked = false,
    string? LockReason = null);
