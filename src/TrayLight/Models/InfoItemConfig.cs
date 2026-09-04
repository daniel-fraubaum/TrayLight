using System.Text.Json.Serialization;

namespace TrayLight.Models;

[JsonConverter(typeof(JsonStringEnumConverter<InfoItemType>))]
public enum InfoItemType
{
    Unknown = 0,
    ComputerName,
    OsVersion,
    LastReboot,
    StorageUsed,
    NetworkInfo,
    SerialNumber,
    IntuneSync
}

public class InfoItemConfig
{
    [JsonPropertyName("type")]
    public InfoItemType Type { get; set; } = InfoItemType.Unknown;

    /// <summary>0..3 places the tile in the top row; -1 hides it.</summary>
    [JsonPropertyName("position")]
    public int Position { get; set; } = -1;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Optional display title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Fluent icon reference, e.g. <c>Segoe Fluent Icons:&amp;#xE946;</c>.</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    // Type-specific options
    [JsonPropertyName("showNotificationBadge")]
    public bool? ShowNotificationBadge { get; set; }

    [JsonPropertyName("uptimeDaysLimit")]
    public int? UptimeDaysLimit { get; set; }

    /// <summary>Warning threshold as a percentage (0-100).</summary>
    [JsonPropertyName("storageLimit")]
    public int? StorageLimit { get; set; }
}
