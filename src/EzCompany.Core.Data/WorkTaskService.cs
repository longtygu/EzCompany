using System.Security.Cryptography;
using EzCompany.Core.Abstractions;
using EzCompany.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EzCompany.Core.Data;

public static class WorkTaskStatuses
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string HandedOver = "HandedOver";
    public const string Completed = "Completed";
    public const string RevisionRequested = "RevisionRequested";
}

public static class WorkTaskKinds
{
    public const string Self = "Self";
    public const string Assigned = "Assigned";
}

public static class WorkTaskPriorities
{
    public const string Low = "Low";
    public const string Normal = "Normal";
    public const string High = "High";
    public const string Urgent = "Urgent";

    public static readonly IReadOnlyList<string> All = [Low, Normal, High, Urgent];
}

public sealed record WorkTaskProgressSummary(
    int TotalTrackedTasks,
    int SelfTodayCount,
    int AssignedOpenCount,
    int HandedOverCount,
    int CompletedCount,
    int OverdueCount,
    double CompletionPercent);

/// <summary>
/// Manages local daily self-tasks, peer-to-peer assigned tasks, result handover attachments,
/// completion metrics, and full chronological assignment/handover audit history.
/// </summary>
public sealed class WorkTaskService
{
    public const long MaxAttachmentSizeBytes = 10 * 1024 * 1024;

    private readonly IDbContextFactory<AppDbContext> _appDbFactory;
    private readonly ILocalStoragePaths _paths;
    private readonly ISessionContext _session;
    private readonly IAuditLogger _audit;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public WorkTaskService(
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

    public async Task<WorkTaskEntity> CreateSelfTaskAsync(
        string title,
        string description,
        string priority,
        DateTimeOffset? dueDateUtc,
        CancellationToken ct = default)
    {
        EnsureActiveSession();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Vui lòng nhập tiêu đề công việc.", nameof(title));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        var entity = new WorkTaskEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = _session.CompanyId,
            TaskKind = WorkTaskKinds.Self,
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Priority = NormalizePriority(priority),
            Status = WorkTaskStatuses.Pending,
            AssignerMachineId = _paths.MachineId,
            AssignerUsername = _session.Username,
            AssignerFullName = _session.FullName,
            AssignerRole = _session.Role,
            AssigneeMachineId = _paths.MachineId,
            AssigneeUsername = _session.Username,
            AssigneeFullName = _session.FullName,
            AssigneeRole = _session.Role,
            DueDateUtc = dueDateUtc ?? new DateTimeOffset(DateTimeOffset.Now.Date.AddHours(17).AddMinutes(30), DateTimeOffset.Now.Offset).ToUniversalTime(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.WorkTasks.Add(entity);
        db.WorkTaskHistories.Add(BuildHistory(entity.Id, "CreatedSelf", "Tạo công việc cá nhân trong ngày.", null, now));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync("TASK_SELF_CREATED", "core.tasks", entity.Title, null, "Info", ct).ConfigureAwait(false);
        _eventBus.Publish(new WorkTaskUpdatedEvent(entity.Id, "CreatedSelf", entity.Title, _session.FullName, now));
        return entity;
    }

    public async Task<WorkTaskEntity> CreateAssignedTaskAsync(
        string assigneeMachineId,
        string assigneeUsername,
        string assigneeFullName,
        string assigneeRole,
        string title,
        string description,
        string priority,
        DateTimeOffset? dueDateUtc,
        CancellationToken ct = default)
    {
        EnsureActiveSession();
        if (string.IsNullOrWhiteSpace(assigneeMachineId) || string.IsNullOrWhiteSpace(assigneeFullName))
        {
            throw new ArgumentException("Vui lòng chọn người nhận việc.", nameof(assigneeMachineId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Vui lòng nhập tiêu đề công việc.", nameof(title));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool isSelfTarget = string.Equals(assigneeMachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase);

        var entity = new WorkTaskEntity
        {
            Id = Guid.NewGuid(),
            CompanyId = _session.CompanyId,
            TaskKind = WorkTaskKinds.Assigned,
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Priority = NormalizePriority(priority),
            Status = WorkTaskStatuses.Pending,
            AssignerMachineId = _paths.MachineId,
            AssignerUsername = _session.Username,
            AssignerFullName = _session.FullName,
            AssignerRole = _session.Role,
            AssigneeMachineId = assigneeMachineId.Trim(),
            AssigneeUsername = assigneeUsername.Trim().ToLowerInvariant(),
            AssigneeFullName = assigneeFullName.Trim(),
            AssigneeRole = string.IsNullOrWhiteSpace(assigneeRole) ? CompanyRoles.Staff : assigneeRole.Trim(),
            DueDateUtc = dueDateUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            PendingSyncTargetMachineId = isSelfTarget ? null : assigneeMachineId.Trim(),
            PendingSyncAction = isSelfTarget ? null : "Assign"
        };

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.WorkTasks.Add(entity);
        db.WorkTaskHistories.Add(BuildHistory(
            entity.Id,
            "Assigned",
            $"Giao việc tới {entity.AssigneeFullName} ({entity.AssigneeRole}).",
            null,
            now));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(
            "TASK_ASSIGNED",
            "core.tasks",
            entity.Title,
            $"To={entity.AssigneeUsername}@{entity.AssigneeMachineId}",
            "Info",
            ct).ConfigureAwait(false);
        _eventBus.Publish(new WorkTaskUpdatedEvent(entity.Id, "Assigned", entity.Title, _session.FullName, now));
        return entity;
    }

    public async Task<WorkTaskEntity> StartTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        EnsureActiveSession();
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        WorkTaskEntity task = await db.WorkTasks.FirstOrDefaultAsync(x => x.Id == taskId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy công việc.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        task.Status = WorkTaskStatuses.InProgress;
        task.UpdatedAtUtc = now;

        if (!string.Equals(task.AssignerMachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
        {
            task.PendingSyncTargetMachineId = task.AssignerMachineId;
            task.PendingSyncAction = "StatusUpdate";
        }

        db.WorkTaskHistories.Add(BuildHistory(task.Id, "Started", "Bắt đầu thực hiện công việc.", null, now));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _eventBus.Publish(new WorkTaskUpdatedEvent(task.Id, "Started", task.Title, _session.FullName, now));
        return task;
    }

    public async Task<WorkTaskEntity> ToggleSelfTaskCompleteAsync(Guid taskId, CancellationToken ct = default)
    {
        EnsureActiveSession();
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        WorkTaskEntity task = await db.WorkTasks.FirstOrDefaultAsync(x => x.Id == taskId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy công việc.");

        if (!string.Equals(task.TaskKind, WorkTaskKinds.Self, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chỉ có thể đánh dấu hoàn thành trực tiếp với task tự tạo.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool completing = !string.Equals(task.Status, WorkTaskStatuses.Completed, StringComparison.Ordinal);
        task.Status = completing ? WorkTaskStatuses.Completed : WorkTaskStatuses.Pending;
        task.CompletedAtUtc = completing ? now : null;
        task.UpdatedAtUtc = now;

        string action = completing ? "Completed" : "Reopened";
        string note = completing ? "Đánh dấu hoàn thành task cá nhân." : "Mở lại task cá nhân.";
        db.WorkTaskHistories.Add(BuildHistory(task.Id, action, note, null, now));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync("TASK_SELF_TOGGLED", "core.tasks", task.Title, $"Status={task.Status}", "Info", ct).ConfigureAwait(false);
        _eventBus.Publish(new WorkTaskUpdatedEvent(task.Id, action, task.Title, _session.FullName, now));
        return task;
    }

    public async Task DeleteSelfTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        EnsureActiveSession();
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        WorkTaskEntity? task = await db.WorkTasks.FirstOrDefaultAsync(x => x.Id == taskId, ct).ConfigureAwait(false);
        if (task is null)
        {
            return;
        }

        if (!string.Equals(task.TaskKind, WorkTaskKinds.Self, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Không thể xóa công việc được giao qua hệ thống.");
        }

        List<WorkTaskHistoryEntity> histories = await db.WorkTaskHistories
            .Where(h => h.TaskId == taskId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        db.WorkTaskHistories.RemoveRange(histories);
        db.WorkTasks.Remove(task);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _eventBus.Publish(new WorkTaskUpdatedEvent(taskId, "Deleted", task.Title, _session.FullName, _timeProvider.GetUtcNow()));
    }

    public async Task<WorkTaskEntity> SubmitHandoverAsync(
        Guid taskId,
        string handoverNotes,
        string? sourceAttachmentFilePath = null,
        CancellationToken ct = default)
    {
        EnsureActiveSession();
        if (string.IsNullOrWhiteSpace(handoverNotes) && string.IsNullOrWhiteSpace(sourceAttachmentFilePath))
        {
            throw new ArgumentException("Vui lòng nhập nội dung bàn giao hoặc chọn tệp kết quả đính kèm.");
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        WorkTaskEntity task = await db.WorkTasks.FirstOrDefaultAsync(x => x.Id == taskId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy công việc.");

        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(sourceAttachmentFilePath))
        {
            if (!File.Exists(sourceAttachmentFilePath))
            {
                throw new FileNotFoundException("Không tìm thấy tệp kết quả bàn giao.", sourceAttachmentFilePath);
            }

            byte[] fileBytes = await File.ReadAllBytesAsync(sourceAttachmentFilePath, ct).ConfigureAwait(false);
            if (fileBytes.Length > MaxAttachmentSizeBytes)
            {
                throw new InvalidOperationException("Tệp đính kèm bàn giao vượt quá giới hạn 10 MB.");
            }

            string safeFileName = SanitizeFileName(Path.GetFileName(sourceAttachmentFilePath));
            string relPath = Path.Combine("Tasks", task.Id.ToString("N"), safeFileName);
            string fullDest = Path.Combine(_paths.VaultDirectory, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullDest)!);
            await File.WriteAllBytesAsync(fullDest, fileBytes, ct).ConfigureAwait(false);

            task.HandoverFileName = safeFileName;
            task.HandoverVaultPath = relPath.Replace('\\', '/');
            task.HandoverFileSha256 = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
        }

        task.HandoverNotes = handoverNotes?.Trim() ?? string.Empty;
        task.HandedOverAtUtc = now;
        task.UpdatedAtUtc = now;

        bool isSelfAssigned = string.Equals(task.TaskKind, WorkTaskKinds.Self, StringComparison.Ordinal) ||
                              string.Equals(task.AssignerMachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase);

        if (isSelfAssigned)
        {
            task.Status = WorkTaskStatuses.Completed;
            task.CompletedAtUtc = now;
            task.PendingSyncTargetMachineId = null;
            task.PendingSyncAction = null;
        }
        else
        {
            task.Status = WorkTaskStatuses.HandedOver;
            task.PendingSyncTargetMachineId = task.AssignerMachineId;
            task.PendingSyncAction = "Handover";
        }

        db.WorkTaskHistories.Add(BuildHistory(
            task.Id,
            "HandedOver",
            string.IsNullOrWhiteSpace(task.HandoverNotes) ? "Đã gửi tệp kết quả bàn giao." : task.HandoverNotes,
            task.HandoverFileName,
            now));

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync(
            "TASK_HANDED_OVER",
            "core.tasks",
            task.Title,
            $"Attachment={task.HandoverFileName ?? "-"}",
            "Info",
            ct).ConfigureAwait(false);
        _eventBus.Publish(new WorkTaskUpdatedEvent(task.Id, "HandedOver", task.Title, _session.FullName, now));
        return task;
    }

    public async Task<WorkTaskEntity> ReviewHandoverAsync(
        Guid taskId,
        bool approveComplete,
        string reviewNotes,
        CancellationToken ct = default)
    {
        EnsureActiveSession();
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        WorkTaskEntity task = await db.WorkTasks.FirstOrDefaultAsync(x => x.Id == taskId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy công việc.");

        if (!string.Equals(task.AssignerMachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Chỉ người giao việc mới có quyền nghiệm thu hoặc yêu cầu làm lại.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        task.ReviewNotes = reviewNotes?.Trim() ?? string.Empty;
        task.UpdatedAtUtc = now;

        if (approveComplete)
        {
            task.Status = WorkTaskStatuses.Completed;
            task.CompletedAtUtc = now;
        }
        else
        {
            task.Status = WorkTaskStatuses.RevisionRequested;
            task.CompletedAtUtc = null;
        }

        bool isSelfTarget = string.Equals(task.AssigneeMachineId, _paths.MachineId, StringComparison.OrdinalIgnoreCase);
        task.PendingSyncTargetMachineId = isSelfTarget ? null : task.AssigneeMachineId;
        task.PendingSyncAction = isSelfTarget ? null : "StatusUpdate";

        string action = approveComplete ? "Completed" : "RevisionRequested";
        string historyNote = approveComplete
            ? (string.IsNullOrWhiteSpace(task.ReviewNotes) ? "Đã nghiệm thu và xác nhận hoàn thành." : $"Nghiệm thu hoàn thành: {task.ReviewNotes}")
            : (string.IsNullOrWhiteSpace(task.ReviewNotes) ? "Yêu cầu chỉnh sửa / làm lại." : $"Yêu cầu làm lại: {task.ReviewNotes}");

        db.WorkTaskHistories.Add(BuildHistory(task.Id, action, historyNote, null, now));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.LogAsync("TASK_REVIEWED", "core.tasks", task.Title, $"Approved={approveComplete}", "Info", ct).ConfigureAwait(false);
        _eventBus.Publish(new WorkTaskUpdatedEvent(task.Id, action, task.Title, _session.FullName, now));
        return task;
    }

    public async Task<WorkTaskEntity> UpsertIncomingSyncedTaskAsync(
        WorkTaskEntity incoming,
        byte[]? attachmentBytes,
        string actorMachineId,
        string actorUsername,
        string actorFullName,
        string syncAction,
        string historyNote,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        string? savedRelVaultPath = null;
        string? savedFileName = null;
        string? savedSha256 = incoming.HandoverFileSha256;

        if (attachmentBytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(incoming.HandoverFileName))
        {
            if (attachmentBytes.Length > MaxAttachmentSizeBytes)
            {
                throw new InvalidOperationException("Tệp bàn giao nhận qua LAN vượt quá 10 MB.");
            }

            string computedSha = Convert.ToHexString(SHA256.HashData(attachmentBytes)).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(incoming.HandoverFileSha256) &&
                !string.Equals(computedSha, incoming.HandoverFileSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException("Mã băm SHA-256 của tệp bàn giao không khớp.");
            }

            savedFileName = SanitizeFileName(incoming.HandoverFileName);
            string relPath = Path.Combine("Tasks", incoming.Id.ToString("N"), savedFileName);
            string fullDest = Path.Combine(_paths.VaultDirectory, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullDest)!);
            await File.WriteAllBytesAsync(fullDest, attachmentBytes, ct).ConfigureAwait(false);
            savedRelVaultPath = relPath.Replace('\\', '/');
            savedSha256 = computedSha;
        }

        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        WorkTaskEntity? existing = await db.WorkTasks.FirstOrDefaultAsync(x => x.Id == incoming.Id, ct).ConfigureAwait(false);

        if (existing is null)
        {
            existing = new WorkTaskEntity
            {
                Id = incoming.Id,
                CompanyId = incoming.CompanyId,
                TaskKind = WorkTaskKinds.Assigned,
                Title = incoming.Title,
                Description = incoming.Description,
                Priority = incoming.Priority,
                Status = incoming.Status,
                AssignerMachineId = incoming.AssignerMachineId,
                AssignerUsername = incoming.AssignerUsername,
                AssignerFullName = incoming.AssignerFullName,
                AssignerRole = incoming.AssignerRole,
                AssigneeMachineId = incoming.AssigneeMachineId,
                AssigneeUsername = incoming.AssigneeUsername,
                AssigneeFullName = incoming.AssigneeFullName,
                AssigneeRole = incoming.AssigneeRole,
                DueDateUtc = incoming.DueDateUtc,
                CreatedAtUtc = incoming.CreatedAtUtc,
                UpdatedAtUtc = incoming.UpdatedAtUtc,
                CompletedAtUtc = incoming.CompletedAtUtc,
                HandoverNotes = incoming.HandoverNotes,
                HandoverFileName = savedFileName ?? incoming.HandoverFileName,
                HandoverVaultPath = savedRelVaultPath ?? existing?.HandoverVaultPath,
                HandoverFileSha256 = savedSha256,
                HandedOverAtUtc = incoming.HandedOverAtUtc,
                ReviewNotes = incoming.ReviewNotes,
                PendingSyncTargetMachineId = null,
                PendingSyncAction = null
            };
            db.WorkTasks.Add(existing);
        }
        else if (incoming.UpdatedAtUtc >= existing.UpdatedAtUtc)
        {
            existing.Title = incoming.Title;
            existing.Description = incoming.Description;
            existing.Priority = incoming.Priority;
            existing.Status = incoming.Status;
            existing.DueDateUtc = incoming.DueDateUtc;
            existing.UpdatedAtUtc = incoming.UpdatedAtUtc;
            existing.CompletedAtUtc = incoming.CompletedAtUtc;
            existing.HandoverNotes = incoming.HandoverNotes;
            existing.HandedOverAtUtc = incoming.HandedOverAtUtc;
            existing.ReviewNotes = incoming.ReviewNotes;
            if (savedFileName is not null)
            {
                existing.HandoverFileName = savedFileName;
                existing.HandoverVaultPath = savedRelVaultPath;
                existing.HandoverFileSha256 = savedSha256;
            }
            existing.PendingSyncTargetMachineId = null;
            existing.PendingSyncAction = null;
        }

        db.WorkTaskHistories.Add(new WorkTaskHistoryEntity
        {
            Id = Guid.NewGuid(),
            TaskId = existing.Id,
            Action = syncAction,
            ActorMachineId = actorMachineId,
            ActorUsername = actorUsername,
            ActorFullName = actorFullName,
            Note = historyNote,
            AttachmentFileName = savedFileName ?? incoming.HandoverFileName,
            TimestampUtc = now
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _audit.LogAsync("TASK_SYNC_RECEIVED", "core.tasks", existing.Title, $"From={actorUsername};Action={syncAction}", "Info", ct)
            .ConfigureAwait(false);
        _eventBus.Publish(new WorkTaskUpdatedEvent(existing.Id, syncAction, existing.Title, actorFullName, now));
        return existing;
    }

    public async Task<IReadOnlyList<WorkTaskEntity>> GetTodaySelfTasksAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        DateTimeOffset todayStartLocal = new(DateTimeOffset.Now.Date, DateTimeOffset.Now.Offset);
        DateTimeOffset tomorrowStartLocal = todayStartLocal.AddDays(1);

        List<WorkTaskEntity> selfTasks = await db.WorkTasks.AsNoTracking()
            .Where(x => x.TaskKind == WorkTaskKinds.Self && x.AssigneeMachineId == _paths.MachineId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return selfTasks
            .Where(x =>
                x.Status != WorkTaskStatuses.Completed ||
                (x.CreatedAtUtc >= todayStartLocal.ToUniversalTime() && x.CreatedAtUtc < tomorrowStartLocal.ToUniversalTime()) ||
                (x.CompletedAtUtc.HasValue && x.CompletedAtUtc.Value >= todayStartLocal.ToUniversalTime()))
            .OrderBy(x => x.Status == WorkTaskStatuses.Completed)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToArray();
    }

    public async Task<IReadOnlyList<WorkTaskEntity>> GetAssignedToMeTasksAsync(bool includeCompleted = false, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<WorkTaskEntity> query = db.WorkTasks.AsNoTracking()
            .Where(x => x.TaskKind == WorkTaskKinds.Assigned && x.AssigneeMachineId == _paths.MachineId);

        if (!includeCompleted)
        {
            query = query.Where(x => x.Status != WorkTaskStatuses.Completed);
        }

        List<WorkTaskEntity> list = await query.ToListAsync(ct).ConfigureAwait(false);
        return list.OrderByDescending(x => x.UpdatedAtUtc).ToArray();
    }

    public async Task<IReadOnlyList<WorkTaskEntity>> GetAssignedByMeTasksAsync(CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<WorkTaskEntity> list = await db.WorkTasks.AsNoTracking()
            .Where(x => x.TaskKind == WorkTaskKinds.Assigned && x.AssignerMachineId == _paths.MachineId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return list.OrderByDescending(x => x.UpdatedAtUtc).ToArray();
    }

    public async Task<IReadOnlyList<WorkTaskEntity>> GetCompletedTasksHistoryAsync(int limit = 50, CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<WorkTaskEntity> list = await db.WorkTasks.AsNoTracking()
            .Where(x => x.Status == WorkTaskStatuses.Completed &&
                        (x.AssigneeMachineId == _paths.MachineId || x.AssignerMachineId == _paths.MachineId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return list
            .OrderByDescending(x => x.CompletedAtUtc ?? x.UpdatedAtUtc)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<WorkTaskHistoryEntity>> GetTaskHistoriesAsync(
        Guid? taskId = null,
        int limit = 150,
        CancellationToken ct = default)
    {
        await using AppDbContext db = await _appDbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        IQueryable<WorkTaskHistoryEntity> query = db.WorkTaskHistories.AsNoTracking();
        if (taskId.HasValue)
        {
            query = query.Where(x => x.TaskId == taskId.Value);
        }

        List<WorkTaskHistoryEntity> items = await query.ToListAsync(ct).ConfigureAwait(false);
        return items.OrderByDescending(x => x.TimestampUtc).Take(limit).ToArray();
    }

    public async Task<WorkTaskProgressSummary> GetProgressSummaryAsync(CancellationToken ct = default)
    {
        IReadOnlyList<WorkTaskEntity> todaySelf = await GetTodaySelfTasksAsync(ct).ConfigureAwait(false);
        IReadOnlyList<WorkTaskEntity> assignedAll = await GetAssignedToMeTasksAsync(includeCompleted: true, ct).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        int selfTodayCount = todaySelf.Count;
        int selfDone = todaySelf.Count(x => x.Status == WorkTaskStatuses.Completed);
        int assignedOpenCount = assignedAll.Count(x => x.Status != WorkTaskStatuses.Completed);
        int assignedDone = assignedAll.Count(x => x.Status == WorkTaskStatuses.Completed);
        int handedOverCount = assignedAll.Count(x => x.Status == WorkTaskStatuses.HandedOver);

        int total = selfTodayCount + assignedAll.Count;
        int completed = selfDone + assignedDone;
        int overdue = todaySelf.Concat(assignedAll)
            .Count(x => x.Status != WorkTaskStatuses.Completed && x.DueDateUtc.HasValue && x.DueDateUtc.Value < now);

        double pct = total == 0 ? 0.0 : Math.Round(completed * 100.0 / total, 1);
        return new WorkTaskProgressSummary(total, selfTodayCount, assignedOpenCount, handedOverCount, completed, overdue, pct);
    }

    private WorkTaskHistoryEntity BuildHistory(
        Guid taskId,
        string action,
        string note,
        string? attachmentFileName,
        DateTimeOffset timestampUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            Action = action,
            ActorMachineId = _paths.MachineId,
            ActorUsername = _session.Username,
            ActorFullName = _session.FullName,
            Note = note,
            AttachmentFileName = attachmentFileName,
            TimestampUtc = timestampUtc
        };

    private void EnsureActiveSession()
    {
        if (!_session.IsAuthenticated || _session.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Phiên làm việc chưa đăng nhập.");
        }
    }

    private static string NormalizePriority(string? priority) =>
        WorkTaskPriorities.All.FirstOrDefault(p => string.Equals(p, priority, StringComparison.OrdinalIgnoreCase))
        ?? WorkTaskPriorities.Normal;

    private static string SanitizeFileName(string fileName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(fileName.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "handover.bin" : cleaned;
    }
}
