namespace EzCompany.Core.Abstractions;

public interface IEventBus
{
    void Publish<TEvent>(TEvent evt);
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
}

public sealed record InternalNoticeEvent(
    Guid NoticeId,
    string SenderAdminName,
    string Title,
    string Message,
    DateTimeOffset SentAtUtc);

public sealed record PeerStatusChangedEvent(
    string MachineId,
    string CompanyId,
    string NodeRole,
    string IpAddress,
    int Port,
    bool IsOnline);

public sealed record PairingRequestReceivedEvent(
    Guid RequestId,
    string MachineId,
    string FullName,
    string Username,
    string SasCode,
    DateTimeOffset RequestedAtUtc);

public sealed record PolicyUpdatedEvent(PolicySnapshot Snapshot);

public sealed record TelemetrySyncCompletedEvent(
    string MachineId,
    int NewPresenceCount,
    int NewUsageCount,
    int NewSnapshotCount,
    DateTimeOffset SyncedAtUtc);

public sealed record UserProfileUpdatedEvent(
    Guid UserId,
    string Username,
    string FullName,
    string Role);

public sealed record WorkTaskUpdatedEvent(
    Guid TaskId,
    string Action,
    string Title,
    string ActorFullName,
    DateTimeOffset UpdatedAtUtc);

public sealed record PasswordResetRequestedEvent(
    Guid RequestId,
    string MachineId,
    string Username,
    string FullName,
    string Reason,
    DateTimeOffset RequestedAtUtc);

public sealed record CompanyLogoUpdatedEvent(
    Guid CompanyId,
    DateTimeOffset UpdatedAtUtc);

public sealed record ChatMessageReceivedEvent(
    Guid ConversationId,
    Guid MessageId,
    string SenderFullName,
    string Preview,
    DateTimeOffset SentAtUtc,
    string SenderMachineId = "",
    string MessageType = "Text");

public sealed record TimelinePlanUpdatedEvent(
    Guid PlanId,
    string Title,
    string ActorFullName,
    DateTimeOffset UpdatedAtUtc);

public sealed record TaskAssignedNotificationEvent(
    Guid TaskId,
    string Title,
    string AssignerFullName,
    string Priority,
    DateTimeOffset? DueDateUtc);

public sealed record UserAvatarUpdatedEvent(
    string MachineId,
    DateTimeOffset UpdatedAtUtc);

public sealed record LeaveRequestUpdatedEvent(
    Guid RequestId,
    string EmployeeName,
    string Status,
    DateTimeOffset UpdatedAtUtc);

public sealed record ExpenseRequestUpdatedEvent(
    Guid RequestId,
    string RequesterName,
    string Status,
    DateTimeOffset UpdatedAtUtc);

public sealed record SharedDocumentUpdatedEvent(
    Guid DocumentId,
    string Title,
    DateTimeOffset UpdatedAtUtc);

public sealed record TaskDeadlineReminderEvent(
    Guid TaskId,
    string Title,
    DateTimeOffset DueDateUtc,
    bool IsOverdue);
