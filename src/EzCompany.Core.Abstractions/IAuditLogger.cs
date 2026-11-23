namespace EzCompany.Core.Abstractions;

public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string MachineId,
    Guid? UserId,
    string Username,
    string ModuleId,
    string Action,
    string? TargetEntity,
    string? DetailsJson,
    string Severity);

/// <summary>
/// Writes structured security and operational audit records into local SQLite storage.
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(
        string action,
        string moduleId,
        string? targetEntity = null,
        string? detailsJson = null,
        string severity = "Info",
        CancellationToken ct = default);

    Task<IReadOnlyList<AuditEntryDto>> GetRecentAsync(int limit = 200, CancellationToken ct = default);
}
