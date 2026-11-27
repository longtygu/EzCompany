using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public static class LeaveRequestTypes
{
    public const string Leave = "Leave";
    public const string HalfDay = "HalfDay";
    public const string Late = "Late";
    public const string Overtime = "Overtime";

    public static readonly IReadOnlyList<string> All = [Leave, HalfDay, Late, Overtime];
}

public static class LeaveRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>
/// Handles employee leave, half-day, late arrival, and overtime requests with LAN P2P synchronization
/// and automatic attendance injection upon Director/HR approval.
/// </summary>
public sealed class LeaveRequestService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly HrAttendanceService _hrService;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public LeaveRequestService(
        IDbContextFactory<AppDbContext> appDbFactory,
        HrAttendanceService hrService,
        ILocalStoragePaths paths,
        ISessionContext session,
        IAuditLogger audit,
        IEventBus eventBus,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _hrService = hrService;
        _paths = paths;
        _session = session;
        _audit = audit;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LeaveRequestEntity> SubmitRequestAsync(
        string requestType,
        DateOnly startDate,
        DateOnly endDate,
        double overtimeHours,
        string reason,
        CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
        {
            throw new InvalidOperationException("Phiên làm việc chưa đăng nhập.");
        }

        if (endDate < startDate)
        {
            throw new ArgumentException("Ngày kết thúc không được nhỏ hơn ngày bắt đầu.", nameof(endDate));
        }

        EmployeeProfileEntity profile = await _hrService.EnsureCurrentUserProfileAsync(ct).ConfigureAwait(false);
        string normalizedType = LeaveRequestTypes.All.FirstOrDefault(t =>
            string.Equals(t, requestType, StringComparison.OrdinalIgnoreCase)) ?? LeaveRequestTypes.Leave;

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var entity = new LeaveRequestEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = _session.CompanyId,
            EmployeeProfileId = profile.Id,
            EmployeeCode = profile.EmployeeCode,
            EmployeeName = profile.FullName,
            MachineId = _paths.MachineId,
            RequestType = normalizedType,
            StartDateKey = startDate.ToString("yyyy-MM-dd"),
            EndDateKey = endDate.ToString("yyyy-MM-dd"),
            OvertimeHours = Math.Round(Math.Clamp(overtimeHours, 0.0, 24.0), 2),
            Reason = reason?.Trim() ?? string.Empty,
            Status = LeaveRequestStatuses.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.LeaveRequests.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(
            "LEAVE_REQUEST_SUBMITTED",
            "core.hr",
            entity.EmployeeCode,
            $"{entity.RequestType} {entity.StartDateKey}..{entity.EndDateKey}",
            "Info",
            ct).ConfigureAwait(false);

        _eventBus.Publish(new LeaveRequestUpdatedEvent(entity.Id, entity.EmployeeName, entity.Status, now));
        return entity;
    }

    public async Task<IReadOnlyList<LeaveRequestEntity>> GetRequestsAsync(
        bool pendingOnly = false,
        bool mineOnly = false,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<LeaveRequestEntity> q = db.LeaveRequests.AsNoTracking();
        if (pendingOnly)
        {
            q = q.Where(r => r.Status == LeaveRequestStatuses.Pending);
        }

        if (mineOnly)
        {
            q = q.Where(r => r.MachineId == _paths.MachineId);
        }

        return await q
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<LeaveRequestEntity> DecideRequestAsync(
        Guid requestId,
        bool approve,
        string reviewNotes,
        CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
        {
            throw new InvalidOperationException("Phiên làm việc chưa đăng nhập.");
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        LeaveRequestEntity req = await db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy đơn xin phép / tăng ca.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        req.Status = approve ? LeaveRequestStatuses.Approved : LeaveRequestStatuses.Rejected;
        req.ReviewedByName = _session.FullName;
        req.ReviewedAtUtc = now;
        req.ReviewNotes = reviewNotes?.Trim() ?? string.Empty;
        req.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (approve)
        {
            Guid profileId = await ResolveEmployeeProfileIdAsync(db, req, ct).ConfigureAwait(false);
            if (profileId != Guid.Empty &&
                DateOnly.TryParse(req.StartDateKey, out DateOnly start) &&
                DateOnly.TryParse(req.EndDateKey, out DateOnly end))
            {
                for (DateOnly d = start; d <= end; d = d.AddDays(1))
                {
                    await ApplyApprovedLeaveDayToAttendanceAsync(profileId, d, req, ct).ConfigureAwait(false);
                }
            }
        }

        await _audit.LogAsync(
            approve ? "LEAVE_REQUEST_APPROVED" : "LEAVE_REQUEST_REJECTED",
            "core.hr",
            req.EmployeeCode,
            $"{req.RequestType} {req.StartDateKey}..{req.EndDateKey} -> {req.Status}",
            "Info",
            ct).ConfigureAwait(false);

        _eventBus.Publish(new LeaveRequestUpdatedEvent(req.Id, req.EmployeeName, req.Status, now));
        return req;
    }

    private async Task ApplyApprovedLeaveDayToAttendanceAsync(
        Guid profileId,
        DateOnly workDate,
        LeaveRequestEntity req,
        CancellationToken ct)
    {
        switch (req.RequestType)
        {
            case LeaveRequestTypes.Leave:
                await _hrService.SaveCustomAttendanceAsync(
                    profileId, workDate, null, null, "Leave", 1.0, 0.0,
                    $"Nghỉ phép đã duyệt: {req.Reason}", ct).ConfigureAwait(false);
                break;

            case LeaveRequestTypes.HalfDay:
                await _hrService.SaveCustomAttendanceAsync(
                    profileId, workDate, new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), "HalfDay", 0.5, 0.0,
                    $"Nghỉ nửa ngày đã duyệt: {req.Reason}", ct).ConfigureAwait(false);
                break;

            case LeaveRequestTypes.Late:
                await _hrService.SaveCustomAttendanceAsync(
                    profileId, workDate, new TimeSpan(9, 0, 0), new TimeSpan(17, 30, 0), "Late", 1.0, 0.0,
                    $"Xin đi muộn đã duyệt: {req.Reason}", ct).ConfigureAwait(false);
                break;

            case LeaveRequestTypes.Overtime:
                AttendanceRecordEntity? existing = await _hrService.GetAttendanceRecordForDayAsync(profileId, workDate, ct)
                    .ConfigureAwait(false);
                TimeSpan? inTime = existing?.CheckInUtc?.ToLocalTime().TimeOfDay ?? new TimeSpan(8, 0, 0);
                TimeSpan? outTime = existing?.CheckOutUtc?.ToLocalTime().TimeOfDay ?? new TimeSpan(17, 30, 0);
                string status = existing?.Status ?? "Present";
                double units = existing?.WorkDayUnits ?? 1.0;
                double totalOt = Math.Round(Math.Clamp((existing?.OvertimeHours ?? 0.0) + req.OvertimeHours, 0.0, 24.0), 2);
                await _hrService.SaveCustomAttendanceAsync(
                    profileId, workDate, inTime, outTime, status, units, totalOt,
                    $"Tăng ca +{req.OvertimeHours:F1}h đã duyệt: {req.Reason}", ct).ConfigureAwait(false);
                break;
        }
    }

    private static async Task<Guid> ResolveEmployeeProfileIdAsync(AppDbContext db, LeaveRequestEntity req, CancellationToken ct)
    {
        EmployeeProfileEntity? byId = await db.EmployeeProfiles.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == req.EmployeeProfileId, ct)
            .ConfigureAwait(false);
        if (byId is not null)
        {
            return byId.Id;
        }

        EmployeeProfileEntity? byMachine = await db.EmployeeProfiles.AsNoTracking()
            .FirstOrDefaultAsync(e => e.LinkedMachineId == req.MachineId || e.EmployeeCode == req.EmployeeCode, ct)
            .ConfigureAwait(false);
        if (byMachine is not null)
        {
            return byMachine.Id;
        }

        return Guid.Empty;
    }

    public async Task<IReadOnlyList<LeaveRequestEntity>> GetAllForSyncAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.LeaveRequests.AsNoTracking()
            .OrderByDescending(r => r.UpdatedAtUtc)
            .Take(200)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> UpsertIncomingRequestsAsync(IEnumerable<LeaveRequestEntity> incoming, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        int changes = 0;
        foreach (LeaveRequestEntity item in incoming)
        {
            LeaveRequestEntity? existing = await db.LeaveRequests.FirstOrDefaultAsync(x => x.Id == item.Id, ct).ConfigureAwait(false);
            if (existing is null)
            {
                db.LeaveRequests.Add(item);
                changes++;
                _eventBus.Publish(new LeaveRequestUpdatedEvent(item.Id, item.EmployeeName, item.Status, item.UpdatedAtUtc));
            }
            else if (item.UpdatedAtUtc > existing.UpdatedAtUtc)
            {
                existing.Status = item.Status;
                existing.ReviewedByName = item.ReviewedByName;
                existing.ReviewedAtUtc = item.ReviewedAtUtc;
                existing.ReviewNotes = item.ReviewNotes;
                existing.Reason = item.Reason;
                existing.UpdatedAtUtc = item.UpdatedAtUtc;
                changes++;
                _eventBus.Publish(new LeaveRequestUpdatedEvent(existing.Id, existing.EmployeeName, existing.Status, existing.UpdatedAtUtc));
            }
        }

        if (changes > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return changes;
    }
}
