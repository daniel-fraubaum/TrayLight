using System.Text.Json.Serialization;

namespace TrayLight.Services.UserSettings;

/// <summary>
/// Per-user preferences persisted at <c>%LOCALAPPDATA%\TrayLight\user-settings.json</c>.
/// Distinct from <see cref="TrayLight.Models.AppConfiguration"/> which is the
/// administrator-managed configuration in <c>%ProgramData%</c>.
/// </summary>
public sealed class UserSettings
{
    /// <summary>True once the welcome dialog has been dismissed with the
    /// "Don't show again" checkbox set.</summary>
    [JsonPropertyName("welcomeShown")]
    public bool WelcomeShown { get; set; }

    /// <summary>Schema version reserved for future migrations.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;
}
