using System.Text.Json.Serialization;

namespace TrayLight.Models;

public class BehaviorConfig
{
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; } = true;

    [JsonPropertyName("refreshIntervalMinutes")]
    public int RefreshIntervalMinutes { get; set; } = 30;

    [JsonPropertyName("showWelcomeScreen")]
    public bool ShowWelcomeScreen { get; set; } = true;

    /// <summary>
    /// Master switch for tile warnings (warning tray icon, tile warning marker,
    /// warning toast). When false, per-tile ShowNotificationBadge is ignored.
    /// See <see cref="Services.Badges.BadgePolicy"/>.
    /// </summary>
    [JsonPropertyName("notifyOnUpdates")]
    public bool NotifyOnUpdates { get; set; } = true;

    /// <summary>
    /// Uptime threshold (days) after which the Last-Reboot tile raises a
    /// warning. <c>0</c> disables the reboot warning entirely. Default: 7.
    /// </summary>
    [JsonPropertyName("rebootWarningDays")]
    public int RebootWarningDays { get; set; } = 7;
}
