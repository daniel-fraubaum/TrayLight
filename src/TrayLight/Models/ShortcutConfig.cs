using System.Text.Json.Serialization;

namespace TrayLight.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ShortcutActionType>))]
public enum ShortcutActionType
{
    Unknown = 0,
    Url,
    App,
    Command,
}

public class ShortcutConfig
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Fluent icon reference, e.g. <c>Segoe Fluent Icons:&amp;#xE8F2;</c>.</summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("actionType")]
    public ShortcutActionType ActionType { get; set; } = ShortcutActionType.Unknown;

    /// <summary>The URL, executable path, or command line to invoke.</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public int Position { get; set; } = -1;

    /// <summary>When true the user must confirm before the action runs.</summary>
    [JsonPropertyName("requiresConfirmation")]
    public bool RequiresConfirmation { get; set; }

    /// <summary>Custom prompt shown in the confirmation dialog.</summary>
    [JsonPropertyName("confirmationMessage")]
    public string? ConfirmationMessage { get; set; }

    /// <summary>Optional toast text shown after a successful command/script run.</summary>
    [JsonPropertyName("successMessage")]
    public string? SuccessMessage { get; set; }
}
