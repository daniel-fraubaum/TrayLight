namespace TrayLight.Services.Actions;

/// <summary>Asks the user to confirm an action; returns true to proceed.</summary>
public interface IConfirmationService
{
    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default);
}
