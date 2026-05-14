using TrayLight.Models;

namespace TrayLight.Services.Configuration;

/// <summary>
/// Reads <see cref="AppConfiguration"/> values from a flat registry layout
/// rooted at <c>HKLM\SOFTWARE\Policies\TrayLight</c>. Missing values are
/// silently filled in from <see cref="AppConfiguration.CreateDefault"/>.
/// </summary>
internal static class RegistryConfigurationReader
{
    private const string Branding   = "Branding";
    private const string Footer     = "Footer";
    private const string Behavior   = "Behavior";
    private const string Logging    = "Logging";
    private const string InfoItems  = "InfoItems";
    private const string Shortcuts  = "Shortcuts";

    public static AppConfiguration Read(IRegistryConfigurationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var defaults = AppConfiguration.CreateDefault();

        return new AppConfiguration
        {
            Branding  = ReadBranding(source, defaults.Branding),
            Footer    = ReadFooter(source, defaults.Footer),
            Behavior  = ReadBehavior(source, defaults.Behavior),
            Logging   = ReadLogging(source, defaults.Logging),
            InfoItems = ReadInfoItems(source),
            Shortcuts = ReadShortcuts(source),
        };
    }

    private static BrandingConfig ReadBranding(IRegistryConfigurationSource s, BrandingConfig d) => new()
    {
        Title       = s.GetString(Branding, "Title")       ?? d.Title,
        Logo        = s.GetString(Branding, "Logo")        ?? d.Logo,
        AccentColor = s.GetString(Branding, "AccentColor") ?? d.AccentColor,
        TrayIcon    = s.GetString(Branding, "TrayIcon")    ?? d.TrayIcon,
        CompanyName = s.GetString(Branding, "CompanyName") ?? d.CompanyName,
    };

    private static FooterConfig ReadFooter(IRegistryConfigurationSource s, FooterConfig d) => new()
    {
        Text         = s.GetString(Footer, "Text") ?? d.Text,
        ShowLastSync = s.GetInt(Footer, "ShowLastSync") is { } b ? b != 0 : d.ShowLastSync,
        InfoText     = s.GetString(Footer, "InfoText") ?? d.InfoText,
    };

    private static BehaviorConfig ReadBehavior(IRegistryConfigurationSource s, BehaviorConfig d) => new()
    {
        AutoStart              = s.GetInt(Behavior, "AutoStart") is { } a              ? a != 0 : d.AutoStart,
        RefreshIntervalMinutes = s.GetInt(Behavior, "RefreshIntervalMinutes")          ?? d.RefreshIntervalMinutes,
        ShowWelcomeScreen      = s.GetInt(Behavior, "ShowWelcomeScreen") is { } w      ? w != 0 : d.ShowWelcomeScreen,
        NotifyOnUpdates        = s.GetInt(Behavior, "NotifyOnUpdates") is { } n        ? n != 0 : d.NotifyOnUpdates,
        RebootWarningDays      = s.GetInt(Behavior, "RebootWarningDays")               ?? d.RebootWarningDays,
    };

    private static LoggingConfig ReadLogging(IRegistryConfigurationSource s, LoggingConfig d) => new()
    {
        EnableEventLog   = s.GetInt(Logging, "EnableEventLog")   is { } e ? e != 0 : d.EnableEventLog,
        EnableFileLog    = s.GetInt(Logging, "EnableFileLog")    is { } f ? f != 0 : d.EnableFileLog,
        LogRetentionDays = s.GetInt(Logging, "LogRetentionDays") ?? d.LogRetentionDays,
        MinimumLevel     = s.GetString(Logging, "MinimumLevel")  ?? d.MinimumLevel,
    };

    private static List<InfoItemConfig> ReadInfoItems(IRegistryConfigurationSource s)
    {
        var list = new List<InfoItemConfig>();
        foreach (var typeName in s.GetSubKeyNames(InfoItems))
        {
            if (!Enum.TryParse<InfoItemType>(typeName, ignoreCase: true, out var type) ||
                type == InfoItemType.Unknown)
                continue;

            var sub = $"{InfoItems}\\{typeName}";
            list.Add(new InfoItemConfig
            {
                Type                  = type,
                Position              = s.GetInt(sub, "Position") ?? -1,
                // Presence of the per-tile subkey means the policy is "Enabled"
                // (Group Policy removes the values when set to Disabled / Not
                // Configured). Legacy "Enabled" REG_DWORD is still honored if
                // explicitly set to 0 to preserve backward compatibility.
                Enabled               = s.GetInt(sub, "Enabled") is { } e ? e != 0 : true,
                Title                 = s.GetString(sub, "Title"),
                Icon                  = s.GetString(sub, "Icon"),
                ShowNotificationBadge = s.GetInt(sub, "ShowNotificationBadge") is { } b ? b != 0 : null,
                UptimeDaysLimit       = s.GetInt(sub, "UptimeDaysLimit"),
                StorageLimit          = s.GetInt(sub, "StorageLimit"),
            });
        }
        return list;
    }

    private static List<ShortcutConfig> ReadShortcuts(IRegistryConfigurationSource s)
    {
        // Shortcuts use ordered numeric subkeys (\0, \1, \2 ...). Out-of-range
        // or non-numeric subkeys are read in alphabetical order after numeric ones.
        var keys  = s.GetSubKeyNames(Shortcuts);
        var sorted = keys
            .Select(k => (Key: k, IsNumber: int.TryParse(k, out var n), Number: int.TryParse(k, out var m) ? m : int.MaxValue))
            .OrderBy(t => t.IsNumber ? 0 : 1)
            .ThenBy(t => t.Number)
            .ThenBy(t => t.Key, StringComparer.OrdinalIgnoreCase);

        var list = new List<ShortcutConfig>();
        foreach (var (key, _, _) in sorted)
        {
            var sub = $"{Shortcuts}\\{key}";
            var actionType = ParseActionType(s.GetString(sub, "ActionType"));
            list.Add(new ShortcutConfig
            {
                Title                = s.GetString(sub, "Title")    ?? string.Empty,
                Subtitle             = s.GetString(sub, "Subtitle") ?? string.Empty,
                Icon                 = s.GetString(sub, "Icon")     ?? string.Empty,
                ActionType           = actionType,
                Action               = s.GetString(sub, "Action")   ?? string.Empty,
                Position             = s.GetInt(sub, "Position")    ?? -1,
                RequiresConfirmation = s.GetInt(sub, "RequiresConfirmation") is { } r && r != 0,
                ConfirmationMessage  = s.GetString(sub, "ConfirmationMessage"),
                SuccessMessage       = s.GetString(sub, "SuccessMessage"),
            });
        }
        return list;
    }

    private static ShortcutActionType ParseActionType(string? value) =>
        Enum.TryParse<ShortcutActionType>(value, ignoreCase: true, out var t) ? t : ShortcutActionType.Unknown;
}
