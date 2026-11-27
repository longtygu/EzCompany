using EzCompany.Core.Abstractions;
using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;

namespace EzCompany.App.Services;

/// <summary>
/// Checks active tasks nearing or past their deadline and publishes <see cref="TaskDeadlineReminderEvent"/>
/// at most once per task per calendar day.
/// </summary>
public sealed class TaskDeadlineReminderService
{
    private readonly WorkTaskService _taskService;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<(Guid TaskId, DateOnly Day)> _notifiedToday = [];
    private readonly object _lock = new();

    public TaskDeadlineReminderService(
        WorkTaskService taskService,
        IEventBus eventBus,
        TimeProvider? timeProvider = null)
    {
        _taskService = taskService;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<int> CheckAndNotifyAsync(TimeSpan? lookAhead = null, CancellationToken ct = default)
    {
        TimeSpan window = lookAhead ?? TimeSpan.FromHours(2);
        IReadOnlyList<WorkTaskEntity> dueTasks = await _taskService
            .GetTasksNearingDeadlineAsync(window, ct)
            .ConfigureAwait(false);

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        int sent = 0;

        foreach (WorkTaskEntity task in dueTasks)
        {
            if (!task.DueDateUtc.HasValue)
            {
                continue;
            }

            lock (_lock)
            {
                if (!_notifiedToday.Add((task.Id, today)))
                {
                    continue;
                }
            }

            bool isOverdue = task.DueDateUtc.Value < nowUtc;
            _eventBus.Publish(new TaskDeadlineReminderEvent(task.Id, task.Title, task.DueDateUtc.Value, isOverdue));
            sent++;
        }

        return sent;
    }
}
