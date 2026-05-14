using TrayLight.Services.Providers;

namespace TrayLight.Services.Badges;

/// <summary>
/// Subscribes to every registered <see cref="IInfoItemProvider"/> and maintains
/// a thread-safe map of <c>typeKey → WarningEntry</c>. Any state transition
/// raises <see cref="BadgeChanged"/>; new warnings additionally raise
/// <see cref="WarningRaised"/> so a one-shot toast can be sent.
/// </summary>
public sealed class NotificationBadgeService : INotificationBadgeService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WarningEntry> _warnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<IInfoItemProvider> _providers;
    private bool _started;
    private bool _disposed;

    public NotificationBadgeService(IEnumerable<IInfoItemProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public BadgeState Current
    {
        get { lock (_gate) return new BadgeState(_warnings.Values.ToArray()); }
    }

    public event EventHandler<BadgeState>? BadgeChanged;
    public event EventHandler<WarningEntry>? WarningRaised;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            foreach (var p in _providers)
                p.DataChanged += OnProviderDataChanged;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
            foreach (var p in _providers)
                p.DataChanged -= OnProviderDataChanged;
        }
    }

    private void OnProviderDataChanged(object? sender, InfoItemData data)
    {
        if (sender is not IInfoItemProvider provider) return;

        WarningEntry? newlyRaised = null;
        bool changed = false;
        BadgeState snapshot;
        lock (_gate)
        {
            var key = provider.Type;
            if (data.HasWarning)
            {
                var entry = new WarningEntry(key, data.Title, data.WarningMessage);
                if (!_warnings.TryGetValue(key, out var existing))
                {
                    _warnings[key] = entry;
                    newlyRaised = entry;
                    changed = true;
                }
                else if (existing != entry)
                {
                    _warnings[key] = entry;
                    changed = true;
                }
            }
            else if (_warnings.Remove(key))
            {
                changed = true;
            }
            snapshot = new BadgeState(_warnings.Values.ToArray());
        }

        if (newlyRaised is not null)
            WarningRaised?.Invoke(this, newlyRaised);
        if (changed)
            BadgeChanged?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }
}
