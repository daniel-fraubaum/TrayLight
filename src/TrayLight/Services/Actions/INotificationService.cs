namespace TrayLight.Services.Actions;

/// <summary>Severity of a user-facing notification.</summary>
public enum NotificationSeverity { Info, Success, Warning, Error }

/// <summary>
/// Cross-cutting service that surfaces short-lived notifications to the user.
/// Default impl uses tray-icon balloons; tests substitute a recording fake.
/// </summary>
public interface INotificationService
{
    void Notify(string title, string message, NotificationSeverity severity = NotificationSeverity.Info);
}
