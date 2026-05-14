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

    /// <summary>When true, the tray icon shows a notification badge for warnings.</summary>
    [JsonPropertyName("notifyOnUpdates")]
    public bool NotifyOnUpdates { get; set; } = true;

    /// <summary>
    /// Uptime threshold (days) after which the Last-Reboot tile raises a
    /// warning. <c>0</c> disables the reboot warning entirely. Default: 7.
    /// </summary>
    [JsonPropertyName("rebootWarningDays")]
    public int RebootWarningDays { get; set; } = 7;
}
