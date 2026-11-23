namespace EzCompany.Core.Data.Entities;

public sealed class PresenceSessionEntity
{
    public Guid Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DateKey { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset EndedAtUtc { get; set; }
    public string State { get; set; } = "Active";
    public long DurationSeconds { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class AppUsageRecordEntity
{
    public Guid Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DateKey { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string SanitizedWindowTitle { get; set; } = string.Empty;
    public long ActiveSeconds { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}

public sealed class EncryptedSnapshotEntity
{
    public Guid Id { get; set; }
    public string MachineId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] ThumbnailEncryptedBlob { get; set; } = [];
    public byte[] FullImageEncryptedBlob { get; set; } = [];
    public int ThumbnailSizeBytes { get; set; }
    public int FullImageSizeBytes { get; set; }
    public bool IsFullDownloadedOnDirector { get; set; }
}

public sealed class SyncCheckpointEntity
{
    public Guid Id { get; set; }
    public string PeerMachineId { get; set; } = string.Empty;
    public DateTimeOffset LastTelemetryPulledUtc { get; set; }
    public DateTimeOffset LastSnapshotPulledUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
