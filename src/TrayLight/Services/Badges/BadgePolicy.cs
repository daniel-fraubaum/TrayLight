using TrayLight.Models;

namespace TrayLight.Services.Badges;

/// <summary>
/// Decides whether a tile's warning may surface (warning tray icon, tile
/// warning marker, warning toast). <c>Behavior\NotifyOnUpdates</c> is the
/// master switch: when it is 0 nothing surfaces, regardless of any per-tile
/// value. Only when it is enabled or not configured does the per-tile
/// <c>InfoItems\&lt;Tile&gt;\ShowNotificationBadge</c> apply (not configured = on).
/// </summary>
public static class BadgePolicy
{
    public static bool IsEnabled(BehaviorConfig behavior, InfoItemConfig? tile) =>
        behavior.NotifyOnUpdates && (tile?.ShowNotificationBadge ?? true);

    /// <summary>
    /// Overload for provider type keys (e.g. <c>lastReboot</c>). A null config
    /// means no policy is loaded yet, which behaves like "not configured".
    /// </summary>
    public static bool IsEnabled(AppConfiguration? config, string typeKey)
    {
        if (config is null) return true;
        var tile = Enum.TryParse<InfoItemType>(typeKey, ignoreCase: true, out var type)
            ? config.InfoItems.FirstOrDefault(i => i.Type == type)
            : null;
        return IsEnabled(config.Behavior, tile);
    }
}
