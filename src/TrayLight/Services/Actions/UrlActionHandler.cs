using System.Diagnostics;
using System.Runtime.Versioning;
using TrayLight.Models;

namespace TrayLight.Services.Actions;

/// <summary>
/// Opens a URL with the registered protocol handler. Permits the schemes the
/// product spec calls out and rejects unknown / dangerous schemes (file:, etc.).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UrlActionHandler : IShortcutActionHandler
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "https", "http", "mailto", "tel", "ms-settings", "microsoft-edge", "companyportal"
    };

    public ShortcutActionType ActionType => ShortcutActionType.Url;

    public bool IsAvailable(ShortcutConfig config) => TryGetScheme(config.Action, out _);

    public Task<ActionResult> ExecuteAsync(ShortcutConfig config, CancellationToken cancellationToken)
    {
        if (!TryGetScheme(config.Action, out var scheme))
            return Task.FromResult(ActionResult.Fail($"URL scheme '{scheme}' is not allowed."));

        try
        {
            Process.Start(new ProcessStartInfo(config.Action) { UseShellExecute = true });
            return Task.FromResult(ActionResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.Fail($"Could not open '{config.Action}'.", ex));
        }
    }

    private static bool TryGetScheme(string url, out string scheme)
    {
        scheme = string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;
        var colon = url.IndexOf(':');
        if (colon <= 0) return false;
        scheme = url[..colon];
        return AllowedSchemes.Contains(scheme);
    }
}
