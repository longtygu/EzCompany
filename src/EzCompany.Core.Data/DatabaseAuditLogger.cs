using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public sealed class DatabaseAuditLogger : IAuditLogger
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ISessionContext _session;

    public DatabaseAuditLogger(IDbContextFactory<AppDbContext> dbFactory, ISessionContext session)
    {
        _dbFactory = dbFactory;
        _session = session;
    }

    public async Task LogAsync(
        string action,
        string moduleId,
        string? targetEntity = null,
        string? detailsJson = null,
        string severity = "Info",
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.AuditLogs.Add(new AuditLogEntity
        {
            Id = Guid.NewGuid(),
            TimestampUtc = DateTimeOffset.UtcNow,
            MachineId = _session.MachineId,
            UserId = _session.UserId == Guid.Empty ? null : _session.UserId,
            Username = string.IsNullOrWhiteSpace(_session.Username) ? "system" : _session.Username,
            ModuleId = moduleId,
            Action = action,
            TargetEntity = targetEntity,
            DetailsJson = detailsJson,
            Severity = severity
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetRecentAsync(int limit = 200, CancellationToken ct = default)
    {
        await using AppDbContext db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(x => x.TimestampUtc)
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(x => new AuditEntryDto(
                x.Id,
                x.TimestampUtc,
                x.MachineId,
                x.UserId,
                x.Username,
                x.ModuleId,
                x.Action,
                x.TargetEntity,
                x.DetailsJson,
                x.Severity))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
