using System.Diagnostics;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace TrayLight.Services.Actions;

/// <summary>
/// Default <see cref="INotificationService"/> that shows balloon tips on the
/// tray icon. The icon is attached lazily by <c>App.OnStartup</c> after the
/// resource has been created. Falls back to <see cref="Debug.WriteLine"/> when
/// no icon is attached (e.g. unit tests).
/// </summary>
public sealed class TrayBalloonNotificationService : INotificationService
{
    private TaskbarIcon? _trayIcon;

    public void Attach(TaskbarIcon trayIcon) => _trayIcon = trayIcon;

    public void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info)
    {
        var icon = severity switch
        {
            NotificationSeverity.Error   => NotificationIcon.Error,
            NotificationSeverity.Warning => NotificationIcon.Warning,
            _                            => NotificationIcon.Info
        };

        var tray = _trayIcon;
        if (tray is null)
        {
            Debug.WriteLine($"[TrayLight notify {severity}] {title}: {message}");
            return;
        }

        try
        {
            tray.ShowNotification(title: title, message: message, icon: icon);
        }
        catch (Exception ex)
        {
            // The shell can refuse balloon tips (focus assist, group policy).
            // Swallow so the executor never bubbles a UX failure.
            Debug.WriteLine($"[TrayLight] ShowNotification failed: {ex.Message}");
        }
    }
}
