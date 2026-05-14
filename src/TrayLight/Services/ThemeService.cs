using Microsoft.Win32;

namespace TrayLight.Services;

public enum AppTheme { Light, Dark }

public interface IThemeService
{
    AppTheme Current { get; }
    event EventHandler? ThemeChanged;
}

/// <summary>
/// Detects the Windows app theme via the registry value
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme</c>
/// and re-reads it whenever the user logs back in or the registry hive changes.
/// </summary>
public class ThemeService : IThemeService, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    private AppTheme _current;
    private System.Threading.Timer? _poll;

    public AppTheme Current => _current;
    public event EventHandler? ThemeChanged;

    public ThemeService()
    {
        _current = ReadCurrent();
        // React instantly to Personalize changes via the system broadcast,
        // and keep a slow safety-net poll for stragglers (RDP, sleep/resume).
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _poll = new System.Threading.Timer(_ => Refresh(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private void OnUserPreferenceChanged(object? sender,
        Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General ||
            e.Category == Microsoft.Win32.UserPreferenceCategory.VisualStyle ||
            e.Category == Microsoft.Win32.UserPreferenceCategory.Color)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var latest = ReadCurrent();
        if (latest != _current)
        {
            _current = latest;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static AppTheme ReadCurrent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue(ValueName) is int v)
            {
                return v == 0 ? AppTheme.Dark : AppTheme.Light;
            }
        }
        catch { /* fall through */ }
        return AppTheme.Light;
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _poll?.Dispose();
        _poll = null;
        GC.SuppressFinalize(this);
    }
}
