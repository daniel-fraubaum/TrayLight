namespace TrayLight.Services.UserSettings;

/// <summary>Loads and persists the per-user settings file.</summary>
public interface IUserSettingsService
{
    /// <summary>Current snapshot. Never null. Cached after the first <see cref="Load"/>.</summary>
    UserSettings Current { get; }

    /// <summary>Re-reads the settings file from disk.</summary>
    UserSettings Load();

    /// <summary>Persists the supplied snapshot atomically.</summary>
    void Save(UserSettings settings);
}
