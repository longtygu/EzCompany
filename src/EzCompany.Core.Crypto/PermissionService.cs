using EzCompany.Core.Abstractions;

namespace EzCompany.Core.Crypto;

public sealed class PermissionService : IPermissionService
{
    private readonly IEventBus _eventBus;
    private readonly object _gate = new();
    private PolicySnapshot? _current;

    public PermissionService(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public bool IsDirector
    {
        get
        {
            lock (_gate)
            {
                return _current is not null &&
                       !_current.IsLocked &&
                       string.Equals(_current.Role, CompanyRoles.Director, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public bool IsLocked
    {
        get
        {
            lock (_gate)
            {
                return _current?.IsLocked ?? true;
            }
        }
    }

    public string? LockReason
    {
        get
        {
            lock (_gate)
            {
                return _current?.LockReason;
            }
        }
    }

    public PolicySnapshot? CurrentPolicy
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<PolicyChangedEventArgs>? PolicyChanged;

    public bool HasModuleAccess(string moduleId)
    {
        lock (_gate)
        {
            if (_current is null || _current.IsLocked)
            {
                return false;
            }

            if (string.Equals(_current.Role, CompanyRoles.Director, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return _current.EnabledModules.Contains(moduleId, StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool HasPermission(string permission)
    {
        lock (_gate)
        {
            if (_current is null || _current.IsLocked)
            {
                return false;
            }

            if (string.Equals(_current.Role, CompanyRoles.Director, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return _current.EnabledPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void ApplyPolicySnapshot(PolicySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            _current = snapshot;
        }

        PolicyChanged?.Invoke(this, new PolicyChangedEventArgs(snapshot));
        _eventBus.Publish(new PolicyUpdatedEvent(snapshot));
    }

    public void Lock(string reason)
    {
        PolicySnapshot? next;
        lock (_gate)
        {
            next = _current is null
                ? null
                : _current with { IsLocked = true, LockReason = reason };
            _current = next;
        }

        PolicyChanged?.Invoke(this, new PolicyChangedEventArgs(next));
        if (next is not null)
        {
            _eventBus.Publish(new PolicyUpdatedEvent(next));
        }
    }
}
