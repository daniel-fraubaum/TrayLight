using System.Runtime.Versioning;
using System.Windows;

namespace TrayLight.Services.Providers;

/// <summary>Tile that shows <see cref="Environment.MachineName"/>.</summary>
[SupportedOSPlatform("windows")]
public sealed class ComputerNameProvider : InfoItemProviderBase
{
    public const string TypeKey = "computerName";

    public override string Type => TypeKey;
    protected override string DefaultTitle => "Computer name";
    protected override string DefaultIcon => "Segoe Fluent Icons:E977"; // Devices

    protected override Task<InfoItemData> CollectAsync(CancellationToken cancellationToken)
    {
        var name = Environment.MachineName;
        var data = new InfoItemData(
            Title: EffectiveTitle,
            Value: name,
            DetailText: $"User: {Environment.UserName}",
            HasWarning: false,
            WarningMessage: string.Empty,
            Icon: EffectiveIcon);
        return Task.FromResult(data);
    }

    protected override Task OnClickAsync(CancellationToken cancellationToken)
    {
        // WPF clipboard must run on a STA thread; the popup VM should normally
        // marshal back to the dispatcher, but we guard against being called on
        // the threadpool by hopping there ourselves.
        var name = Environment.MachineName;
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => SafeSetClipboard(name));
        }
        else
        {
            SafeSetClipboard(name);
        }
        return Task.CompletedTask;
    }

    private void SafeSetClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) { LogError("Clipboard.SetText failed", ex); }
    }
}
