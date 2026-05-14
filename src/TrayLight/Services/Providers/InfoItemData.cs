namespace TrayLight.Services.Providers;

/// <summary>
/// Result of an <see cref="IInfoItemProvider"/> data refresh.
/// All string fields are guaranteed non-null (empty string when missing).
/// </summary>
public record InfoItemData(
    string Title,
    string Value,
    string DetailText,
    bool HasWarning,
    string WarningMessage,
    string Icon)
{
    /// <summary>A safe "Not available" fallback used when collection fails.</summary>
    public static InfoItemData Unavailable(string title, string icon, string? reason = null) =>
        new(title, "Not available", reason ?? string.Empty, false, string.Empty, icon);
}
