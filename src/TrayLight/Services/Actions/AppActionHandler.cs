using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using TrayLight.Models;

namespace TrayLight.Services.Actions;

/// <summary>
/// Launches a Win32 .exe or a UWP / packaged app via its <c>shell:AppsFolder</c>
/// activation moniker.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AppActionHandler : IShortcutActionHandler
{
    private const string AppsFolderPrefix = "shell:AppsFolder";

    public ShortcutActionType ActionType => ShortcutActionType.App;

    public bool IsAvailable(ShortcutConfig config)
    {
        var target = config.Action;
        if (string.IsNullOrWhiteSpace(target)) return false;

        // shell: URIs cannot be probed cheaply -> trust them.
        if (target.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
            return true;

        // Plain executable path: must exist on disk.
        try { return File.Exists(target); }
        catch { return false; }
    }

    public Task<ActionResult> ExecuteAsync(ShortcutConfig config, CancellationToken cancellationToken)
    {
        var target = config.Action;
        if (string.IsNullOrWhiteSpace(target))
            return Task.FromResult(ActionResult.Fail("No application path configured."));

        try
        {
            // explorer.exe is the documented way to activate shell:AppsFolder
            // entries because Process.Start chokes on the colon-bearing AUMID.
            var psi = target.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true }
                : new ProcessStartInfo(target) { UseShellExecute = true };

            Process.Start(psi);
            return Task.FromResult(ActionResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.Fail($"Could not start '{target}'.", ex));
        }
    }
}
