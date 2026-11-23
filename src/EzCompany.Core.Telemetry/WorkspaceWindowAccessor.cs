using System.Windows;

namespace EzCompany.Core.Telemetry;

/// <summary>
/// Holds a weak reference to the active <c>WorkspaceWindow</c> so Method B capture
/// exclusively renders the application's own window without touching the OS desktop.
/// </summary>
public interface IWorkspaceWindowAccessor
{
    void RegisterWindow(Window window);
    void UnregisterWindow(Window window);
    Window? GetActiveWindow();
}

public sealed class WorkspaceWindowAccessor : IWorkspaceWindowAccessor
{
    private readonly object _gate = new();
    private WeakReference<Window>? _windowRef;

    public void RegisterWindow(Window window)
    {
        lock (_gate)
        {
            _windowRef = new WeakReference<Window>(window);
        }
    }

    public void UnregisterWindow(Window window)
    {
        lock (_gate)
        {
            if (_windowRef is not null && _windowRef.TryGetTarget(out Window? current) && ReferenceEquals(current, window))
            {
                _windowRef = null;
            }
        }
    }

    public Window? GetActiveWindow()
    {
        lock (_gate)
        {
            return _windowRef is not null && _windowRef.TryGetTarget(out Window? window) ? window : null;
        }
    }
}
