using System.ComponentModel;
using TrayLight.Models;
using TrayLight.Services.Providers;

namespace TrayLight.Services.Badges;

/// <summary>
/// Subscribes to every registered <see cref="IInfoItemProvider"/> and maintains
/// a thread-safe map of <c>typeKey → WarningEntry</c>. Any state transition
/// raises <see cref="BadgeChanged"/>; new warnings additionally raise
/// <see cref="WarningRaised"/> so a one-shot toast can be sent.
/// A provider warning only counts when <see cref="BadgePolicy"/> allows it, and
/// every warning is re-evaluated when the configuration changes.
/// </summary>
public sealed class NotificationBadgeService : INotificationBadgeService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WarningEntry> _warnings = new(StringComparer.OrdinalIgnoreCase);
    // Last data per provider, so a policy change can be re-applied without
    // waiting for the provider's next refresh.
    private readonly Dictionary<string, InfoItemData> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<IInfoItemProvider> _providers;
    private readonly IConfigurationService? _config;
    private bool _started;
    private bool _disposed;

    public NotificationBadgeService(IEnumerable<IInfoItemProvider> providers, IConfigurationService? config = null)
    {
        _providers = providers.ToArray();
        _config = config;
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
            if (_config is not null)
                _config.PropertyChanged += OnConfigChanged;
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
            if (_config is not null)
                _config.PropertyChanged -= OnConfigChanged;
        }
    }

    private void OnProviderDataChanged(object? sender, InfoItemData data)
    {
        if (sender is not IInfoItemProvider provider) return;

        var config = _config?.Current;
        var raised = new List<WarningEntry>();
        bool changed;
        BadgeState snapshot;
        lock (_gate)
        {
            _latest[provider.Type] = data;
            changed = Apply(provider.Type, data, config, raised);
            snapshot = new BadgeState(_warnings.Values.ToArray());
        }
        Publish(raised, changed, snapshot);
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IConfigurationService.Current)) return;

        var config = _config?.Current;
        var raised = new List<WarningEntry>();
        var changed = false;
        BadgeState snapshot;
        lock (_gate)
        {
            foreach (var (key, data) in _latest)
                changed |= Apply(key, data, config, raised);
            snapshot = new BadgeState(_warnings.Values.ToArray());
        }
        Publish(raised, changed, snapshot);
    }

    /// <summary>
    /// Updates the warning entry for <paramref name="key"/>. Returns true when
    /// the set changed; new warnings are added to <paramref name="raised"/>.
    /// Caller must hold <see cref="_gate"/>.
    /// </summary>
    private bool Apply(string key, InfoItemData data, AppConfiguration? config, List<WarningEntry> raised)
    {
        if (!data.HasWarning || !BadgePolicy.IsEnabled(config, key))
            return _warnings.Remove(key);

        var entry = new WarningEntry(key, data.Title, data.WarningMessage);
        if (!_warnings.TryGetValue(key, out var existing))
        {
            _warnings[key] = entry;
            raised.Add(entry);
            return true;
        }
        if (existing != entry)
        {
            _warnings[key] = entry;
            return true;
        }
        return false;
    }

    private void Publish(List<WarningEntry> raised, bool changed, BadgeState snapshot)
    {
        foreach (var entry in raised)
            WarningRaised?.Invoke(this, entry);
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
