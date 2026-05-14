using Microsoft.Win32;

namespace TrayLight.Services;

/// <summary>
/// Registers the application in HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// so it launches automatically at user logon. For elevated/system-wide auto-start
/// a Scheduled Task can be used instead — see docs/README.md.
/// </summary>
public class AutoStartService : IAutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TrayLight";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                     ?? Registry.CurrentUser.CreateSubKey(RunKey);
        var exePath = Environment.ProcessPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
