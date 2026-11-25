using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public static class TimelineCategories
{
    public const string Plan = "Plan";
    public const string Milestone = "Milestone";
    public const string Meeting = "Meeting";
    public const string Deadline = "Deadline";

    public static readonly IReadOnlyList<string> All = [Plan, Milestone, Meeting, Deadline];
}

/// <summary>
/// Manages shared company schedule/timeline plans visible to and editable by both Directors and Employees.
/// Supports last-write-wins P2P synchronization across all LAN peers.
/// </summary>
public sealed class TimelinePlanService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public TimelinePlanService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        IAuditLogger audit,
        IEventBus eventBus,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
        _audit = audit;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<TimelinePlanEntity>> GetActivePlansAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<TimelinePlanEntity> items = await db.TimelinePlans.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return items
            .OrderBy(p => p.StartDateUtc)
            .ThenBy(p => p.EndDateUtc)
            .ToArray();
    }

    public async Task<IReadOnlyList<TimelinePlanEntity>> GetAllForSyncAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.TimelinePlans.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<TimelinePlanEntity> AddOrUpdatePlanAsync(
        Guid? id,
        string title,
        string description,
        string category,
        DateTimeOffset startDateUtc,
        DateTimeOffset endDateUtc,
        string status = "Planned",
        CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated || _session.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Phiên làm việc chưa đăng nhập.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Vui lòng nhập tiêu đề lịch trình / kế hoạch.", nameof(title));
        }

        if (endDateUtc < startDateUtc)
        {
            endDateUtc = startDateUtc;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        TimelinePlanEntity? entity = id.HasValue
            ? await db.TimelinePlans.FirstOrDefaultAsync(x => x.Id == id.Value, ct).ConfigureAwait(false)
            : null;

        if (entity is null)
        {
            entity = new TimelinePlanEntity
            {
                Id = id ?? Guid.NewGuid(),
                CompanyId = _session.CompanyId,
                Title = title.Trim(),
                Description = description?.Trim() ?? string.Empty,
                Category = NormalizeCategory(category),
                StartDateUtc = startDateUtc,
                EndDateUtc = endDateUtc,
                Status = string.IsNullOrWhiteSpace(status) ? "Planned" : status,
                CreatedByMachineId = _paths.MachineId,
                CreatedByUsername = _session.Username,
                CreatedByFullName = _session.FullName,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsDeleted = false
            };
            db.TimelinePlans.Add(entity);
        }
        else
        {
            entity.Title = title.Trim();
            entity.Description = description?.Trim() ?? string.Empty;
            entity.Category = NormalizeCategory(category);
            entity.StartDateUtc = startDateUtc;
            entity.EndDateUtc = endDateUtc;
            entity.Status = string.IsNullOrWhiteSpace(status) ? entity.Status : status;
            entity.UpdatedAtUtc = now;
            entity.IsDeleted = false;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _audit.LogAsync("TIMELINE_PLAN_SAVED", "core.timeline", entity.Title, $"Category={entity.Category};Status={entity.Status}", "Info", ct)
            .ConfigureAwait(false);
        _eventBus.Publish(new TimelinePlanUpdatedEvent(entity.Id, entity.Title, _session.FullName, now));
        return entity;
    }

    public async Task<TimelinePlanEntity?> CycleStatusAsync(Guid planId, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        TimelinePlanEntity? entity = await db.TimelinePlans.FirstOrDefaultAsync(x => x.Id == planId, ct).ConfigureAwait(false);
        if (entity is null || entity.IsDeleted)
        {
            return null;
        }

        entity.Status = entity.Status switch
        {
            "Planned" => "InProgress",
            "InProgress" => "Done",
            _ => "Planned"
        };
        entity.UpdatedAtUtc = _timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _eventBus.Publish(new TimelinePlanUpdatedEvent(entity.Id, entity.Title, _session.FullName, entity.UpdatedAtUtc));
        return entity;
    }

    public async Task DeletePlanAsync(Guid planId, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        TimelinePlanEntity? entity = await db.TimelinePlans.FirstOrDefaultAsync(x => x.Id == planId, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = _timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _eventBus.Publish(new TimelinePlanUpdatedEvent(entity.Id, entity.Title, _session.FullName, entity.UpdatedAtUtc));
    }

    public async Task<int> UpsertIncomingPlansAsync(
        IEnumerable<TimelinePlanEntity> incoming,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (company is null)
        {
            return 0;
        }

        int changes = 0;
        TimelinePlanEntity? lastChanged = null;

        foreach (TimelinePlanEntity item in incoming)
        {
            if (item.CompanyId != company.CompanyId)
            {
                continue;
            }

            TimelinePlanEntity? local = await db.TimelinePlans.FirstOrDefaultAsync(x => x.Id == item.Id, ct).ConfigureAwait(false);
            if (local is null)
            {
                db.TimelinePlans.Add(new TimelinePlanEntity
                {
                    Id = item.Id,
                    CompanyId = item.CompanyId,
                    Title = item.Title,
                    Description = item.Description,
                    Category = item.Category,
                    StartDateUtc = item.StartDateUtc,
                    EndDateUtc = item.EndDateUtc,
                    Status = item.Status,
                    CreatedByMachineId = item.CreatedByMachineId,
                    CreatedByUsername = item.CreatedByUsername,
                    CreatedByFullName = item.CreatedByFullName,
                    CreatedAtUtc = item.CreatedAtUtc,
                    UpdatedAtUtc = item.UpdatedAtUtc,
                    IsDeleted = item.IsDeleted
                });
                lastChanged = item;
                changes++;
            }
            else if (item.UpdatedAtUtc > local.UpdatedAtUtc)
            {
                local.Title = item.Title;
                local.Description = item.Description;
                local.Category = item.Category;
                local.StartDateUtc = item.StartDateUtc;
                local.EndDateUtc = item.EndDateUtc;
                local.Status = item.Status;
                local.UpdatedAtUtc = item.UpdatedAtUtc;
                local.IsDeleted = item.IsDeleted;
                lastChanged = local;
                changes++;
            }
        }

        if (changes > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (lastChanged is not null)
            {
                _eventBus.Publish(new TimelinePlanUpdatedEvent(
                    lastChanged.Id,
                    lastChanged.Title,
                    lastChanged.CreatedByFullName,
                    lastChanged.UpdatedAtUtc));
            }
        }

        return changes;
    }

    private static string NormalizeCategory(string? category) =>
        TimelineCategories.All.FirstOrDefault(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase))
        ?? TimelineCategories.Plan;
}
