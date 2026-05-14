using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using TrayLight.Models;
using TrayLight.Services.Logging;

namespace TrayLight.Services.Actions;

/// <summary>
/// Strategy-pattern coordinator that resolves the right
/// <see cref="IShortcutActionHandler"/> for a config, optionally asks for
/// confirmation, runs it, logs the result, and surfaces success/failure
/// notifications.
/// </summary>
public sealed class ActionExecutor : IActionExecutor
{
    private readonly Dictionary<ShortcutActionType, IShortcutActionHandler> _handlers;
    private readonly IConfirmationService _confirmation;
    private readonly INotificationService _notifications;
    private readonly ILogger<ActionExecutor> _logger;

    public ActionExecutor(
        IEnumerable<IShortcutActionHandler> handlers,
        IConfirmationService confirmation,
        INotificationService notifications,
        ILogger<ActionExecutor>? logger = null)
    {
        // Last-registered handler wins (allows overrides in tests / DI).
        _handlers = handlers.ToDictionary(h => h.ActionType, h => h);
        _confirmation = confirmation;
        _notifications = notifications;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionExecutor>.Instance;
    }

    /// <summary>
    /// True when the shortcut should be visible in the popup. Returns false
    /// for unknown action types and for app shortcuts whose target is missing.
    /// </summary>
    public bool IsVisible(ShortcutConfig config) =>
        _handlers.TryGetValue(config.ActionType, out var h) && h.IsAvailable(config);

    public async Task<ActionResult> ExecuteAsync(ShortcutConfig config, CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(config.ActionType, out var handler))
        {
            var msg = $"Unsupported action type '{config.ActionType}'.";
            _notifications.Notify(config.Title, msg, NotificationSeverity.Error);
            return ActionResult.Fail(msg);
        }

        if (config.RequiresConfirmation)
        {
            var prompt = string.IsNullOrWhiteSpace(config.ConfirmationMessage)
                ? $"Run '{config.Title}'?"
                : config.ConfirmationMessage!;
            var confirmed = await _confirmation
                .ConfirmAsync(config.Title, prompt, cancellationToken)
                .ConfigureAwait(false);
            if (!confirmed)
                return ActionResult.Fail("Cancelled by user.");
        }

        Log(InvokeEventId, EventLogEntryType.Information,
            $"Action '{config.Title}' ({config.ActionType}) -> {config.Action}");

        ActionResult result;
        try
        {
            result = await handler.ExecuteAsync(config, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ActionResult.Fail($"Unhandled exception in {config.ActionType} handler.", ex);
        }

        if (result.Success)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
                _notifications.Notify(config.Title, result.Message!, NotificationSeverity.Success);
        }
        else
        {
            Log(FailureEventId, EventLogEntryType.Warning,
                $"Action '{config.Title}' failed: {result.Message} {result.Error}");
            _notifications.Notify(
                config.Title,
                result.Message ?? "Action failed.",
                NotificationSeverity.Error);
        }

        return result;
    }

    private void Log(int eventId, EventLogEntryType type, string message)
    {
        // Pre-existing internal helper preserved as a thin shim that forwards
        // to the structured logger so the canonical Event Ids (3000/3001) are
        // emitted in one place.
        if (eventId == InvokeEventId)
            _logger.LogInformation(LogEvents.ActionExecuted, "{Message}", message);
        else if (type == EventLogEntryType.Warning || type == EventLogEntryType.Error)
            _logger.LogWarning(LogEvents.ActionFailed, "{Message}", message);
        else
            _logger.LogInformation(LogEvents.ActionExecuted, "{Message}", message);
    }

    private const int InvokeEventId = 3000;
    private const int FailureEventId = 3001;
}
