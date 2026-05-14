using System.Text.Json.Serialization;

namespace TrayLight.Models;

/// <summary>
/// Controls how the app emits diagnostics. All values have sensible defaults
/// so existing config files keep working without modification.
/// </summary>
public class LoggingConfig
{
    /// <summary>When true, structured events are written to the Windows Application log.</summary>
    [JsonPropertyName("enableEventLog")]
    public bool EnableEventLog { get; set; } = true;

    /// <summary>When true, log entries are appended to a daily rolling file under %LOCALAPPDATA%.</summary>
    [JsonPropertyName("enableFileLog")]
    public bool EnableFileLog { get; set; } = true;

    /// <summary>How many days of rolling log files to retain. Older files are deleted on startup.</summary>
    [JsonPropertyName("logRetentionDays")]
    public int LogRetentionDays { get; set; } = 7;

    /// <summary>
    /// Lowest <see cref="Microsoft.Extensions.Logging.LogLevel"/> that gets written.
    /// Accepts the standard names: Trace, Debug, Information, Warning, Error, Critical, None.
    /// </summary>
    [JsonPropertyName("minimumLevel")]
    public string MinimumLevel { get; set; } = "Information";
}
