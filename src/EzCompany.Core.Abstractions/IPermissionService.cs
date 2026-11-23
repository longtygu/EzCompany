namespace EzCompany.Core.Abstractions;

public sealed class PolicyChangedEventArgs : EventArgs
{
    public PolicyChangedEventArgs(PolicySnapshot? snapshot)
    {
        Snapshot = snapshot;
    }

    public PolicySnapshot? Snapshot { get; }
}

/// <summary>
/// Evaluates module and granular permission grants against the active verified policy license.
/// </summary>
public interface IPermissionService
{
    bool IsDirector { get; }
    bool IsLocked { get; }
    string? LockReason { get; }
    PolicySnapshot? CurrentPolicy { get; }

    bool HasModuleAccess(string moduleId);
    bool HasPermission(string permission);

    void ApplyPolicySnapshot(PolicySnapshot snapshot);
    void Lock(string reason);

    event EventHandler<PolicyChangedEventArgs>? PolicyChanged;
}
