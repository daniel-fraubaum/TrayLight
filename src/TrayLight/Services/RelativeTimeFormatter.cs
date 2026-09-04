using TrayLight.Resources;

namespace TrayLight.Services;

/// <summary>
/// Single source of truth for the localized "relative time" strings shown on
/// the info tiles, the tray hover tooltip and the shortcut placeholders. Both
/// the popup tile and the <c>LastReboot</c>/<c>IntuneSync</c> placeholder
/// resolvers format through here, so a placeholder always renders exactly what
/// its tile shows (e.g. "2u 9m geleden" on Dutch systems) instead of a stale or
/// hardcoded value.
/// </summary>
public static class RelativeTimeFormatter
{
    /// <summary>Time since last boot: "2h 9m ago" / "3d 4h ago" / "just now" (localized).</summary>
    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 2)    return Strings.Format("UptimeDaysHoursAgoFormat", (int)uptime.TotalDays, uptime.Hours);
        if (uptime.TotalDays >= 1)    return Strings.Format("UptimeOneDayHoursAgoFormat", uptime.Hours);
        if (uptime.TotalHours >= 1)   return Strings.Format("UptimeHoursMinutesAgoFormat", uptime.Hours, uptime.Minutes);
        if (uptime.TotalMinutes >= 1) return Strings.Format("UptimeMinutesAgoFormat", (int)uptime.TotalMinutes);
        return Strings.RelativeJustNow;
    }

    /// <summary>
    /// Elapsed time since an event: "5 minutes ago" / "2h 3m ago" / "just now"
    /// (localized). Negative or absurdly large values collapse to "unknown".
    /// </summary>
    public static string FormatRelative(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero || elapsed.TotalDays > 3650) return Strings.RelativeUnknown;
        if (elapsed.TotalMinutes < 1) return Strings.RelativeJustNow;
        if (elapsed.TotalMinutes < 60)
        {
            var m = (int)elapsed.TotalMinutes;
            return Strings.Format(m == 1 ? "RelativeMinuteAgoFormat" : "RelativeMinutesAgoFormat", m);
        }
        if (elapsed.TotalHours < 24)
            return Strings.Format("RelativeHoursMinutesAgoFormat", elapsed.Hours, elapsed.Minutes);
        var d = (int)elapsed.TotalDays;
        return Strings.Format(d == 1 ? "RelativeDayAgoFormat" : "RelativeDaysAgoFormat", d);
    }
}
