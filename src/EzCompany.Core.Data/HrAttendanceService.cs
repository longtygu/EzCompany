using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public sealed record MonthlyAttendanceSummaryRow(
    Guid EmployeeProfileId,
    string EmployeeCode,
    string EmployeeName,
    int PresentDays,
    int LateDays,
    int HalfDays,
    int LeaveDays,
    int AbsentDays,
    double TotalWorkedHours);

/// <summary>
/// Manages local employee HR profiles, daily check-in/check-out attendance tracking,
/// and monthly attendance summaries ready for Excel reporting.
/// </summary>
public sealed class HrAttendanceService
{
    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _timeProvider;

    public HrAttendanceService(
        IDbContextFactory<AppDbContext> appDbFactory,
        ILocalStoragePaths paths,
        ISessionContext session,
        IAuditLogger audit,
        TimeProvider? timeProvider = null)
    {
        _appDbFactory = appDbFactory;
        _paths = paths;
        _session = session;
        _audit = audit;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<EmployeeProfileEntity>> GetEmployeesAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.EmployeeProfiles.AsNoTracking()
            .OrderBy(e => e.EmployeeCode)
            .ThenBy(e => e.FullName)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<EmployeeProfileEntity> SaveEmployeeAsync(EmployeeProfileEntity profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.EmployeeCode))
        {
            throw new ArgumentException("Vui lòng nhập mã nhân viên.", nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(profile.FullName))
        {
            throw new ArgumentException("Vui lòng nhập họ tên nhân viên.", nameof(profile));
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        CompanyProfileEntity? company = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        Guid companyId = _session.CompanyId != Guid.Empty ? _session.CompanyId : company?.CompanyId ?? Guid.Empty;
        DateTimeOffset now = _timeProvider.GetUtcNow();

        EmployeeProfileEntity? existing = profile.Id != Guid.Empty
            ? await db.EmployeeProfiles.FirstOrDefaultAsync(e => e.Id == profile.Id, ct).ConfigureAwait(false)
            : null;

        string normalizedCode = profile.EmployeeCode.Trim().ToUpperInvariant();
        string formattedRoles = CompanyRoles.FormatRoleStorage(CompanyRoles.ParseRoles(profile.RolesCsv));

        if (existing is null)
        {
            bool duplicate = await db.EmployeeProfiles.AnyAsync(e => e.EmployeeCode == normalizedCode, ct).ConfigureAwait(false);
            if (duplicate)
            {
                throw new InvalidOperationException($"Mã nhân viên '{normalizedCode}' đã tồn tại.");
            }

            existing = new EmployeeProfileEntity
            {
                Id = profile.Id == Guid.Empty ? Guid.NewGuid() : profile.Id,
                CompanyId = companyId,
                EmployeeCode = normalizedCode,
                FullName = profile.FullName.Trim(),
                Department = profile.Department?.Trim() ?? string.Empty,
                PositionTitle = profile.PositionTitle?.Trim() ?? string.Empty,
                RolesCsv = formattedRoles,
                LinkedMachineId = string.IsNullOrWhiteSpace(profile.LinkedMachineId) ? null : profile.LinkedMachineId.Trim(),
                LinkedUsername = string.IsNullOrWhiteSpace(profile.LinkedUsername) ? null : profile.LinkedUsername.Trim().ToLowerInvariant(),
                Phone = profile.Phone?.Trim() ?? string.Empty,
                Email = profile.Email?.Trim() ?? string.Empty,
                NationalId = profile.NationalId?.Trim() ?? string.Empty,
                JoinDateUtc = profile.JoinDateUtc == default ? now : profile.JoinDateUtc,
                EmploymentStatus = string.IsNullOrWhiteSpace(profile.EmploymentStatus) ? "Active" : profile.EmploymentStatus.Trim(),
                Notes = profile.Notes?.Trim() ?? string.Empty,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.EmployeeProfiles.Add(existing);
        }
        else
        {
            existing.EmployeeCode = normalizedCode;
            existing.FullName = profile.FullName.Trim();
            existing.Department = profile.Department?.Trim() ?? string.Empty;
            existing.PositionTitle = profile.PositionTitle?.Trim() ?? string.Empty;
            existing.RolesCsv = formattedRoles;
            existing.LinkedMachineId = string.IsNullOrWhiteSpace(profile.LinkedMachineId) ? null : profile.LinkedMachineId.Trim();
            existing.LinkedUsername = string.IsNullOrWhiteSpace(profile.LinkedUsername) ? null : profile.LinkedUsername.Trim().ToLowerInvariant();
            existing.Phone = profile.Phone?.Trim() ?? string.Empty;
            existing.Email = profile.Email?.Trim() ?? string.Empty;
            existing.NationalId = profile.NationalId?.Trim() ?? string.Empty;
            existing.EmploymentStatus = string.IsNullOrWhiteSpace(profile.EmploymentStatus) ? "Active" : profile.EmploymentStatus.Trim();
            existing.Notes = profile.Notes?.Trim() ?? string.Empty;
            existing.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _audit.LogAsync("HR_EMPLOYEE_SAVED", "core.hr", existing.EmployeeCode, existing.FullName, "Info", ct).ConfigureAwait(false);
        return existing;
    }

    public async Task DeleteEmployeeAsync(Guid employeeId, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        EmployeeProfileEntity? existing = await db.EmployeeProfiles.FirstOrDefaultAsync(e => e.Id == employeeId, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        db.EmployeeProfiles.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _audit.LogAsync("HR_EMPLOYEE_DELETED", "core.hr", existing.EmployeeCode, existing.FullName, "Warning", ct).ConfigureAwait(false);
    }

    public async Task<int> SyncEmployeesFromPairedDevicesAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<PairedDeviceEntity> paired = await db.PairedDevices.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        if (paired.Count == 0)
        {
            return 0;
        }

        int added = 0;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (PairedDeviceEntity dev in paired)
        {
            EmployeeProfileEntity? exists = await db.EmployeeProfiles
                .FirstOrDefaultAsync(e => e.LinkedMachineId == dev.MachineId, ct)
                .ConfigureAwait(false);

            if (exists is null)
            {
                string code = $"NV-{dev.MachineId.Replace("EZ-", string.Empty, StringComparison.OrdinalIgnoreCase)[..Math.Min(6, dev.MachineId.Length)]}";
                bool codeTaken = await db.EmployeeProfiles.AnyAsync(e => e.EmployeeCode == code, ct).ConfigureAwait(false);
                if (codeTaken)
                {
                    code = $"{code}-{added + 1}";
                }

                db.EmployeeProfiles.Add(new EmployeeProfileEntity
                {
                    Id = Guid.NewGuid(),
                    CompanyId = dev.CompanyId,
                    EmployeeCode = code,
                    FullName = dev.FullName,
                    Department = string.Empty,
                    PositionTitle = CompanyRoles.FormatRolesBilingual(dev.Role),
                    RolesCsv = CompanyRoles.FormatRoleStorage(CompanyRoles.ParseRoles(dev.Role)),
                    LinkedMachineId = dev.MachineId,
                    LinkedUsername = dev.Username,
                    JoinDateUtc = dev.PairedAtUtc == default ? now : dev.PairedAtUtc,
                    EmploymentStatus = "Active",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
                added++;
            }
            else
            {
                exists.FullName = dev.FullName;
                exists.LinkedUsername = dev.Username;
                exists.RolesCsv = CompanyRoles.FormatRoleStorage(CompanyRoles.ParseRoles(dev.Role));
                exists.UpdatedAtUtc = now;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return added;
    }

    public async Task<EmployeeProfileEntity> EnsureCurrentUserProfileAsync(CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
        {
            throw new InvalidOperationException("Phiên làm việc chưa đăng nhập.");
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        EmployeeProfileEntity? profile = await db.EmployeeProfiles
            .FirstOrDefaultAsync(e => e.LinkedMachineId == _paths.MachineId, ct)
            .ConfigureAwait(false);

        if (profile is not null)
        {
            return profile;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string suffix = _paths.MachineId.Replace("EZ-", string.Empty, StringComparison.OrdinalIgnoreCase);
        string code = $"NV-{suffix[..Math.Min(6, suffix.Length)]}";
        profile = new EmployeeProfileEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = _session.CompanyId,
            EmployeeCode = code,
            FullName = _session.FullName,
            Department = string.Empty,
            PositionTitle = CompanyRoles.FormatRolesBilingual(_session.Role),
            RolesCsv = CompanyRoles.FormatRoleStorage(CompanyRoles.ParseRoles(_session.Role)),
            LinkedMachineId = _paths.MachineId,
            LinkedUsername = _session.Username,
            JoinDateUtc = now,
            EmploymentStatus = "Active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.EmployeeProfiles.Add(profile);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return profile;
    }

    public async Task<AttendanceRecordEntity> CheckInTodayAsync(CancellationToken ct = default)
    {
        EmployeeProfileEntity profile = await EnsureCurrentUserProfileAsync(ct).ConfigureAwait(false);
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        DateTimeOffset localNow = nowUtc.ToLocalTime();
        string dateKey = localNow.ToString("yyyy-MM-dd");

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        AttendanceRecordEntity? rec = await db.AttendanceRecords
            .FirstOrDefaultAsync(r => r.EmployeeProfileId == profile.Id && r.DateKey == dateKey, ct)
            .ConfigureAwait(false);

        if (rec is null)
        {
            string status = localNow.TimeOfDay > new TimeSpan(8, 30, 0) ? "Late" : "Present";
            rec = new AttendanceRecordEntity
            {
                Id = Guid.NewGuid(),
                CompanyId = profile.CompanyId,
                EmployeeProfileId = profile.Id,
                EmployeeCode = profile.EmployeeCode,
                EmployeeName = profile.FullName,
                MachineId = _paths.MachineId,
                DateKey = dateKey,
                CheckInUtc = nowUtc,
                Status = status,
                Notes = status == "Late" ? $"Đi muộn ({localNow:HH:mm})" : $"Vào ca ({localNow:HH:mm})",
                UpdatedAtUtc = nowUtc
            };
            db.AttendanceRecords.Add(rec);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await _audit.LogAsync("HR_CHECK_IN", "core.hr", rec.EmployeeCode, $"{dateKey} {localNow:HH:mm}", "Info", ct).ConfigureAwait(false);
        }

        return rec;
    }

    public async Task<AttendanceRecordEntity> CheckOutTodayAsync(CancellationToken ct = default)
    {
        EmployeeProfileEntity profile = await EnsureCurrentUserProfileAsync(ct).ConfigureAwait(false);
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        DateTimeOffset localNow = nowUtc.ToLocalTime();
        string dateKey = localNow.ToString("yyyy-MM-dd");

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        AttendanceRecordEntity? rec = await db.AttendanceRecords
            .FirstOrDefaultAsync(r => r.EmployeeProfileId == profile.Id && r.DateKey == dateKey, ct)
            .ConfigureAwait(false);

        if (rec is null)
        {
            rec = new AttendanceRecordEntity
            {
                Id = Guid.NewGuid(),
                CompanyId = profile.CompanyId,
                EmployeeProfileId = profile.Id,
                EmployeeCode = profile.EmployeeCode,
                EmployeeName = profile.FullName,
                MachineId = _paths.MachineId,
                DateKey = dateKey,
                CheckInUtc = nowUtc,
                CheckOutUtc = nowUtc,
                WorkedHours = 0,
                Status = "HalfDay",
                Notes = $"Ra ca không có check-in ({localNow:HH:mm})",
                UpdatedAtUtc = nowUtc
            };
            db.AttendanceRecords.Add(rec);
        }
        else
        {
            rec.CheckOutUtc = nowUtc;
            if (rec.CheckInUtc.HasValue)
            {
                rec.WorkedHours = Math.Round(Math.Max(0.0, (nowUtc - rec.CheckInUtc.Value).TotalHours), 2);
            }
            rec.Notes = string.IsNullOrWhiteSpace(rec.Notes)
                ? $"Ra ca ({localNow:HH:mm})"
                : $"{rec.Notes} • Ra ca ({localNow:HH:mm})";
            rec.UpdatedAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _audit.LogAsync("HR_CHECK_OUT", "core.hr", rec.EmployeeCode, $"{dateKey} Worked={rec.WorkedHours:F2}h", "Info", ct).ConfigureAwait(false);
        return rec;
    }

    public async Task<AttendanceRecordEntity> UpsertAttendanceRecordAsync(
        AttendanceRecordEntity record,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        EmployeeProfileEntity? emp = await db.EmployeeProfiles
            .FirstOrDefaultAsync(e => e.EmployeeCode == record.EmployeeCode || (!string.IsNullOrEmpty(record.MachineId) && e.LinkedMachineId == record.MachineId), ct)
            .ConfigureAwait(false);

        Guid empProfileId = emp?.Id ?? (record.EmployeeProfileId == Guid.Empty ? Guid.NewGuid() : record.EmployeeProfileId);
        if (emp is null)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            emp = new EmployeeProfileEntity
            {
                Id = empProfileId,
                CompanyId = record.CompanyId,
                EmployeeCode = record.EmployeeCode,
                FullName = record.EmployeeName,
                LinkedMachineId = record.MachineId,
                RolesCsv = CompanyRoles.Staff,
                JoinDateUtc = now,
                EmploymentStatus = "Active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.EmployeeProfiles.Add(emp);
        }

        AttendanceRecordEntity? existing = await db.AttendanceRecords
            .FirstOrDefaultAsync(r => r.EmployeeProfileId == empProfileId && r.DateKey == record.DateKey, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new AttendanceRecordEntity
            {
                Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
                CompanyId = record.CompanyId,
                EmployeeProfileId = empProfileId,
                EmployeeCode = emp.EmployeeCode,
                EmployeeName = emp.FullName,
                MachineId = record.MachineId,
                DateKey = record.DateKey,
                CheckInUtc = record.CheckInUtc,
                CheckOutUtc = record.CheckOutUtc,
                WorkedHours = record.WorkedHours,
                Status = record.Status,
                Notes = record.Notes,
                UpdatedAtUtc = record.UpdatedAtUtc
            };
            db.AttendanceRecords.Add(existing);
        }
        else if (record.UpdatedAtUtc >= existing.UpdatedAtUtc)
        {
            existing.CheckInUtc = record.CheckInUtc;
            existing.CheckOutUtc = record.CheckOutUtc;
            existing.WorkedHours = record.WorkedHours;
            existing.Status = record.Status;
            existing.Notes = record.Notes;
            existing.UpdatedAtUtc = record.UpdatedAtUtc;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return existing;
    }

    public async Task<IReadOnlyList<AttendanceRecordEntity>> GetAttendanceRecordsAsync(
        int year,
        int month,
        Guid? employeeProfileId = null,
        CancellationToken ct = default)
    {
        string prefix = $"{year:D4}-{month:D2}-";
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<AttendanceRecordEntity> query = db.AttendanceRecords.AsNoTracking()
            .Where(r => r.DateKey.StartsWith(prefix));

        if (employeeProfileId.HasValue)
        {
            query = query.Where(r => r.EmployeeProfileId == employeeProfileId.Value);
        }

        return await query
            .OrderByDescending(r => r.DateKey)
            .ThenBy(r => r.EmployeeCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MonthlyAttendanceSummaryRow>> GetMonthlySummaryAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        IReadOnlyList<EmployeeProfileEntity> employees = await GetEmployeesAsync(ct).ConfigureAwait(false);
        IReadOnlyList<AttendanceRecordEntity> records = await GetAttendanceRecordsAsync(year, month, null, ct).ConfigureAwait(false);

        ILookup<Guid, AttendanceRecordEntity> byEmp = records.ToLookup(r => r.EmployeeProfileId);
        var rows = new List<MonthlyAttendanceSummaryRow>();

        foreach (EmployeeProfileEntity emp in employees)
        {
            List<AttendanceRecordEntity> empRecs = byEmp[emp.Id].ToList();
            rows.Add(new MonthlyAttendanceSummaryRow(
                emp.Id,
                emp.EmployeeCode,
                emp.FullName,
                PresentDays: empRecs.Count(r => r.Status == "Present"),
                LateDays: empRecs.Count(r => r.Status == "Late"),
                HalfDays: empRecs.Count(r => r.Status == "HalfDay"),
                LeaveDays: empRecs.Count(r => r.Status == "Leave"),
                AbsentDays: empRecs.Count(r => r.Status == "Absent"),
                TotalWorkedHours: Math.Round(empRecs.Sum(r => r.WorkedHours), 2)));
        }

        return rows;
    }
}
