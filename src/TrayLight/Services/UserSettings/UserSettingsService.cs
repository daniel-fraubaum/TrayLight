using System.IO;
using System.Text.Json;

namespace TrayLight.Services.UserSettings;

/// <summary>
/// File-backed implementation of <see cref="IUserSettingsService"/>. The
/// settings file lives in <c>%LOCALAPPDATA%\TrayLight\</c> so each Windows
/// user keeps their own preferences (relevant on shared/kiosk devices).
/// All file IO is wrapped in try/catch — a corrupt or missing file simply
/// resets the user to the defaults rather than crashing the app.
/// </summary>
public sealed class UserSettingsService : IUserSettingsService
{
    public const string FolderName = "TrayLight";
    public const string FileName   = "user-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly object _gate = new();
    private UserSettings _current = new();
    private bool _loaded;

    public UserSettingsService() : this(DefaultFilePath()) { }

    /// <summary>Test seam: override the on-disk location.</summary>
    internal UserSettingsService(string filePath)
    {
        _filePath = filePath;
    }

    public UserSettings Current
    {
        get { if (!_loaded) Load(); return _current; }
    }

    public UserSettings Load()
    {
        lock (_gate)
        {
            _loaded = true;
            try
            {
                if (!File.Exists(_filePath))
                {
                    _current = new UserSettings();
                    return _current;
                }
                var json = File.ReadAllText(_filePath);
                _current = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
            }
            catch
            {
                _current = new UserSettings();
            }
            return _current;
        }
    }

    public void Save(UserSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            // Atomic-ish write: serialize to a temp file then move into place
            // so a torn write can never leave the user with a half-written
            // settings file that fails to parse on next launch.
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            if (File.Exists(_filePath)) File.Replace(tmp, _filePath, destinationBackupFileName: null);
            else File.Move(tmp, _filePath);

            _current = settings;
            _loaded = true;
        }
    }

    internal static string DefaultFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FolderName,
            FileName);
}
