using System.Text.Json.Serialization;

namespace TrayLight.Models;

/// <summary>
/// Strongly-typed root of the application's runtime configuration. Values
/// are populated from the registry under
/// <c>HKLM\SOFTWARE\Policies\TrayLight\</c> by
/// <see cref="TrayLight.Services.Configuration.RegistryConfigurationReader"/>;
/// any missing keys fall back to <see cref="CreateDefault"/>.
/// </summary>
public class AppConfiguration
{
    [JsonPropertyName("branding")]
    public BrandingConfig Branding { get; set; } = new();

    [JsonPropertyName("infoItems")]
    public List<InfoItemConfig> InfoItems { get; set; } = new();

    [JsonPropertyName("shortcuts")]
    public List<ShortcutConfig> Shortcuts { get; set; } = new();

    [JsonPropertyName("footer")]
    public FooterConfig Footer { get; set; } = new();

    [JsonPropertyName("behavior")]
    public BehaviorConfig Behavior { get; set; } = new();

    [JsonPropertyName("logging")]
    public LoggingConfig Logging { get; set; } = new();

    /// <summary>
    /// Returns a configuration populated with safe defaults that allow the
    /// app to start successfully even when no policy/registry config is
    /// present. Six tiles are enabled out of the box: Computer Name,
    /// OS Version, Last Reboot, Storage Usage (warns at 90%), Serial
    /// Number and Intune Sync.
    /// </summary>
    public static AppConfiguration CreateDefault() => new()
    {
        Branding = new BrandingConfig
        {
            Title = "IT Support",
            AccentColor = "#0078D4",
            CompanyName = string.Empty
        },
        Footer = new FooterConfig
        {
            Text = string.Empty,
            ShowLastSync = true
        },
        Behavior = new BehaviorConfig
        {
            AutoStart = true,
            RefreshIntervalMinutes = 30,
            ShowWelcomeScreen = true,
            NotifyOnUpdates = true
        },
        InfoItems = new List<InfoItemConfig>
        {
            new() { Type = InfoItemType.ComputerName,  Position = 0, Enabled = true },
            new() { Type = InfoItemType.OsVersion,     Position = 1, Enabled = true },
            new() { Type = InfoItemType.LastReboot,    Position = 2, Enabled = true },
            new() { Type = InfoItemType.StorageUsed,   Position = 3, Enabled = true, StorageLimit = 90 },
            new() { Type = InfoItemType.SerialNumber,  Position = 4, Enabled = true },
            new() { Type = InfoItemType.IntuneSync,    Position = 5, Enabled = true },
        },
        Shortcuts = new List<ShortcutConfig>(),
        Logging   = new LoggingConfig()
    };
}
