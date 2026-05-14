using System.Windows;

namespace TrayLight.Services.Actions;

/// <summary>WPF <see cref="MessageBox"/>-based confirmation prompt.</summary>
public sealed class MessageBoxConfirmationService : IConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        bool Show()
        {
            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);
            return result == MessageBoxResult.OK;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return Task.FromResult(Show());
        return Task.FromResult(dispatcher.Invoke(Show));
    }
}
