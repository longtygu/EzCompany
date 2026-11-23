using EzCompany.Core.Abstractions;

namespace EzCompany.Core.LanAgent.Models;

public sealed record WorkstationHealthDto(
    string MachineId,
    Guid CompanyId,
    string AppVersion,
    int SchemaVersion,
    long DbSizeBytes,
    long VaultSizeBytes,
    double FreeDiskPercent,
    long ActivePolicyVersion,
    bool IsPolicyLocked,
    string? PolicyLockReason,
    DateTimeOffset UtcNow);

public sealed record VaultFileEntryDto(
    string RelativePath,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);

public sealed record NoticeRequestDto(
    Guid NoticeId,
    string Title,
    string Message,
    DateTimeOffset SentAtUtc);

public sealed record RegistryDeviceSyncDto(
    Guid CompanyId,
    string MachineId,
    string DevicePublicKeyBase64,
    Guid UserId,
    string Username,
    string FullName,
    string Role,
    long CurrentPolicyVersion,
    string CurrentPolicyJson,
    string? PendingPolicyJson,
    string? LastKnownIp,
    int LastKnownPort,
    string AppVersion,
    int SchemaVersion,
    DateTimeOffset PairedAtUtc,
    string PairedByAdminName);

public sealed record TelemetryPresenceDto(
    Guid Id,
    string MachineId,
    Guid UserId,
    string Username,
    string DateKey,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string State,
    long DurationSeconds,
    DateTimeOffset UpdatedAtUtc);

public sealed record TelemetryAppUsageDto(
    Guid Id,
    string MachineId,
    Guid UserId,
    string Username,
    string DateKey,
    string ProcessName,
    string SanitizedWindowTitle,
    long ActiveSeconds,
    DateTimeOffset LastSeenUtc);

public sealed record TelemetrySnapshotMetaDto(
    Guid Id,
    string MachineId,
    Guid UserId,
    string Username,
    DateTimeOffset CapturedAtUtc,
    int Width,
    int Height,
    byte[] ThumbnailEncryptedBlob,
    int ThumbnailSizeBytes,
    int FullImageSizeBytes);

public sealed record TelemetryDeltaResponseDto(
    string MachineId,
    DateTimeOffset ServerUtc,
    IReadOnlyList<TelemetryPresenceDto> PresenceSessions,
    IReadOnlyList<TelemetryAppUsageDto> AppUsages,
    IReadOnlyList<TelemetrySnapshotMetaDto> Snapshots);

public sealed record TelemetryFullSnapshotDto(
    Guid SnapshotId,
    string MachineId,
    DateTimeOffset CapturedAtUtc,
    byte[] FullImageEncryptedBlob);

public sealed record ApplyPolicyRequestDto(string SignedPolicyJson);

public sealed record ApplyPolicyResponseDto(bool Applied, long PolicyVersion, string Message);
