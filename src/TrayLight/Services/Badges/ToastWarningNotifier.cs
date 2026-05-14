using TrayLight.Services.Actions;

namespace TrayLight.Services.Badges;

/// <summary>
/// Subscribes to <see cref="INotificationBadgeService.WarningRaised"/> and
/// forwards each new warning to <see cref="INotificationService"/> (the tray
/// balloon / toast surface) — but never more often than
/// <see cref="ThrottlePerType"/> for the same provider type.
/// </summary>
public sealed class ToastWarningNotifier : IDisposable
{
    public static readonly TimeSpan ThrottlePerType = TimeSpan.FromHours(1);

    private readonly INotificationBadgeService _badges;
    private readonly INotificationService _notifier;
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, DateTime> _lastSent = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _disposed;

    public ToastWarningNotifier(INotificationBadgeService badges, INotificationService notifier)
        : this(badges, notifier, () => DateTime.UtcNow) { }

    internal ToastWarningNotifier(
        INotificationBadgeService badges,
        INotificationService notifier,
        Func<DateTime> clock)
    {
        _badges = badges;
        _notifier = notifier;
        _clock = clock;
        _badges.WarningRaised += OnWarningRaised;
    }

    private void OnWarningRaised(object? sender, WarningEntry e)
    {
        var now = _clock();
        lock (_gate)
        {
            if (_lastSent.TryGetValue(e.TypeKey, out var last) &&
                now - last < ThrottlePerType)
            {
                return;
            }
            _lastSent[e.TypeKey] = now;
        }

        var message = string.IsNullOrWhiteSpace(e.Message)
            ? $"{e.Title} requires attention."
            : e.Message;
        _notifier.Notify(e.Title, message, NotificationSeverity.Warning);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _badges.WarningRaised -= OnWarningRaised;
    }
}
