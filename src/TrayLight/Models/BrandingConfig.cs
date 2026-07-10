using System.Text.Json.Serialization;

namespace TrayLight.Models;

public class BrandingConfig
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "TrayLight";

    /// <summary>HTTP(S) URL, local file path, or Base64-encoded image data.</summary>
    [JsonPropertyName("logo")]
    public string Logo { get; set; } = string.Empty;

    /// <summary>Hex color string, e.g. <c>#0078D4</c>.</summary>
    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#0078D4";

    /// <summary>Optional path to a custom .ico file used as the tray icon.</summary>
    [JsonPropertyName("trayIcon")]
    public string TrayIcon { get; set; } = string.Empty;

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// When <c>true</c>, hides the "Powered by headsinthecloud.blog" line in the
    /// popup footer. Default <c>false</c> (the line is shown). The About dialog
    /// attribution is never affected by this setting.
    /// </summary>
    [JsonPropertyName("hideAttribution")]
    public bool HideAttribution { get; set; }
}
