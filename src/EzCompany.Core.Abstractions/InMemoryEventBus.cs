using System.Collections.Concurrent;

namespace EzCompany.Core.Abstractions;

/// <summary>
/// Thread-safe in-process pub/sub event bus connecting background LAN/telemetry agents with UI views.
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _gate = new();

    public void Publish<TEvent>(TEvent evt)
    {
        List<Delegate>? snapshot;
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out List<Delegate>? list) || list.Count == 0)
            {
                return;
            }

            snapshot = new List<Delegate>(list);
        }

        foreach (Delegate handler in snapshot)
        {
            if (handler is Action<TEvent> typed)
            {
                typed(evt);
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            List<Delegate> list = _handlers.GetOrAdd(typeof(TEvent), _ => []);
            list.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(typeof(TEvent), out List<Delegate>? list))
                {
                    list.Remove(handler);
                }
            }
        });
    }

    private sealed class Subscription(Action disposeAction) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                disposeAction();
            }
        }
    }
}
