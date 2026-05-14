using TrayLight.Models;

namespace TrayLight.Services.Actions;

/// <summary>Outcome of an <see cref="IShortcutActionHandler.ExecuteAsync"/> call.</summary>
public sealed record ActionResult(bool Success, string? Message = null, Exception? Error = null)
{
    public static ActionResult Ok(string? message = null) => new(true, message);
    public static ActionResult Fail(string message, Exception? ex = null) => new(false, message, ex);
}

/// <summary>Strategy for executing a single <see cref="ShortcutActionType"/>.</summary>
public interface IShortcutActionHandler
{
    /// <summary>The action type this handler is responsible for.</summary>
    ShortcutActionType ActionType { get; }

    /// <summary>
    /// Returns false to indicate the shortcut should not even be displayed
    /// (e.g. its target executable does not exist on this machine).
    /// </summary>
    bool IsAvailable(ShortcutConfig config);

    Task<ActionResult> ExecuteAsync(ShortcutConfig config, CancellationToken cancellationToken);
}
