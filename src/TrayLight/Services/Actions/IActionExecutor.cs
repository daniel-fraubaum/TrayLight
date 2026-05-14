using TrayLight.Models;

namespace TrayLight.Services.Actions;

/// <summary>Public facade over the registered <see cref="IShortcutActionHandler"/>s.</summary>
public interface IActionExecutor
{
    /// <summary>True when the shortcut should be visible in the UI.</summary>
    bool IsVisible(ShortcutConfig config);

    /// <summary>Runs the configured action with confirmation + notification plumbing.</summary>
    Task<ActionResult> ExecuteAsync(ShortcutConfig config, CancellationToken cancellationToken = default);
}
