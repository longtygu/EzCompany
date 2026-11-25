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

public sealed record PeerDirectoryEntryDto(
    string MachineId,
    string Username,
    string FullName,
    string Role,
    bool IsOnline,
    string? Endpoint);

public sealed record ProfileUpdateNotificationDto(
    string MachineId,
    string Username,
    string FullName);

public sealed record TaskSyncPayloadDto(
    Guid TaskId,
    Guid CompanyId,
    string TaskKind,
    string Title,
    string Description,
    string Priority,
    string Status,
    string AssignerMachineId,
    string AssignerUsername,
    string AssignerFullName,
    string AssignerRole,
    string AssigneeMachineId,
    string AssigneeUsername,
    string AssigneeFullName,
    string AssigneeRole,
    DateTimeOffset? DueDateUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string HandoverNotes,
    string? HandoverFileName,
    string? HandoverFileSha256,
    string? HandoverFileBase64,
    DateTimeOffset? HandedOverAtUtc,
    string ReviewNotes,
    string SyncAction,
    string HistoryNote,
    string SenderSignedPolicyJson,
    string? AssignmentFileName = null,
    string? AssignmentFileSha256 = null,
    string? AssignmentFileBase64 = null);

public sealed record TaskSyncAckDto(bool Accepted, string Message);

public sealed record CompanyBrandingDto(
    Guid CompanyId,
    string? CompanyLogoBase64,
    DateTimeOffset? UpdatedAtUtc);

public sealed record ChatSyncEnvelopeDto(
    Guid ConversationId,
    Guid CompanyId,
    string ConversationType,
    string ConversationName,
    string MemberMachineIdsJson,
    string MemberNamesJson,
    Guid? PinnedTaskId,
    string? PinnedTaskSummary,
    Guid MessageId,
    string SenderMachineId,
    string SenderUsername,
    string SenderFullName,
    string Content,
    string? AttachmentFileName,
    string? AttachmentSha256,
    string? AttachmentBase64,
    long AttachmentSizeBytes,
    Guid? LinkedTaskId,
    DateTimeOffset SentAtUtc,
    string SenderSignedPolicyJson);

public sealed record ChatHistoryPullResponseDto(IReadOnlyList<ChatSyncEnvelopeDto> Messages);

public sealed record TimelinePlanDto(
    Guid Id,
    Guid CompanyId,
    string Title,
    string Description,
    string Category,
    DateTimeOffset StartDateUtc,
    DateTimeOffset EndDateUtc,
    string Status,
    string CreatedByMachineId,
    string CreatedByUsername,
    string CreatedByFullName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsDeleted);

public sealed record TimelineSyncRequestDto(
    IReadOnlyList<TimelinePlanDto> Plans,
    string SenderSignedPolicyJson);

public sealed record AttendanceSyncDto(
    Guid Id,
    Guid CompanyId,
    string EmployeeCode,
    string EmployeeName,
    string MachineId,
    string DateKey,
    DateTimeOffset? CheckInUtc,
    DateTimeOffset? CheckOutUtc,
    double WorkedHours,
    string Status,
    string Notes,
    DateTimeOffset UpdatedAtUtc,
    string SenderSignedPolicyJson);

public sealed record AccountResetInitiateRequestDto(
    string MachineId,
    string Username,
    string FullName,
    string? RequestedNewUsername,
    string Reason);

public sealed record AccountResetInitiateResponseDto(
    Guid RequestId,
    string State,
    string Message,
    DateTimeOffset ExpiresAtUtc);

public sealed record AccountResetStatusResponseDto(
    Guid RequestId,
    string State,
    string? DecisionMessage,
    string? SignedResetTicketJson);
