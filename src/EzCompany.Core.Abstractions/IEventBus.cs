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
