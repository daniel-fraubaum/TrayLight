namespace TrayLight.Services.Badges;

/// <summary>
/// Aggregates the warning flags from every <see cref="Providers.IInfoItemProvider"/>
/// and exposes a single observable view that the tray icon and the popup can
/// bind to.
/// </summary>
public interface INotificationBadgeService : IDisposable
{
    /// <summary>Current aggregate snapshot. Never null.</summary>
    BadgeState Current { get; }

    /// <summary>Raised on any change (warning added, removed, or message updated).</summary>
    event EventHandler<BadgeState>? BadgeChanged;

    /// <summary>Raised exactly once per *new* warning that wasn't already active.</summary>
    event EventHandler<WarningEntry>? WarningRaised;

    /// <summary>Begin observing the provider set. Idempotent.</summary>
    void Start();

    /// <summary>Stop observing. Idempotent.</summary>
    void Stop();
}
