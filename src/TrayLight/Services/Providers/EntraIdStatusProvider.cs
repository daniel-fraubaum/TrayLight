using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace TrayLight.Services.Providers;

/// <summary>
/// Reports Entra ID (Azure AD) join state by parsing <c>dsregcmd /status</c>
/// output. Slow (~200ms) so the result is cached by the base class.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EntraIdStatusProvider : InfoItemProviderBase
{
    public const string TypeKey = "entraIdStatus";

    public override string Type => TypeKey;
    protected override string DefaultTitle => "Identity";
    protected override string DefaultIcon => "Segoe Fluent Icons:E910"; // Contact

    private readonly Func<CancellationToken, Task<string>> _dsregcmdRunner;

    public EntraIdStatusProvider() : this(RunDsregcmdAsync) { }

    internal EntraIdStatusProvider(Func<CancellationToken, Task<string>> dsregcmdRunner)
    {
        _dsregcmdRunner = dsregcmdRunner;
    }

    protected override async Task<InfoItemData> CollectAsync(CancellationToken cancellationToken)
    {
        var output = await _dsregcmdRunner(cancellationToken).ConfigureAwait(false);
        var parsed = Parse(output);

        return new InfoItemData(
            Title: EffectiveTitle,
            Value: parsed.StateDisplay,
            DetailText: string.IsNullOrEmpty(parsed.TenantName)
                ? string.Empty
                : $"Tenant: {parsed.TenantName}",
            // Identity tile is informational - join state is shown but never
            // surfaced as a warning.
            HasWarning: false,
            WarningMessage: string.Empty,
            Icon: EffectiveIcon);
    }

    protected override Task OnClickAsync(CancellationToken cancellationToken)
    {
        LaunchShell("ms-settings:workplace");
        return Task.CompletedTask;
    }

    internal static ParsedStatus Parse(string dsregcmdOutput)
    {
        bool azureAdJoined = false, domainJoined = false, workplaceJoined = false;
        string? tenantName = null;

        foreach (var raw in dsregcmdOutput.Split('\n'))
        {
            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (key.Equals("AzureAdJoined", StringComparison.OrdinalIgnoreCase))
                azureAdJoined = value.StartsWith("YES", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals("DomainJoined", StringComparison.OrdinalIgnoreCase))
                domainJoined = value.StartsWith("YES", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals("WorkplaceJoined", StringComparison.OrdinalIgnoreCase))
                workplaceJoined = value.StartsWith("YES", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals("TenantName", StringComparison.OrdinalIgnoreCase))
                tenantName = value;
        }

        var (state, display) = (azureAdJoined, domainJoined, workplaceJoined) switch
        {
            (true, true, _)  => (JoinState.HybridJoined, "Hybrid Joined"),
            (true, false, _) => (JoinState.EntraJoined,  "Entra ID Joined"),
            (false, _, true) => (JoinState.Registered,   "Registered"),
            _                => (JoinState.NotJoined,    "Not Joined")
        };

        return new ParsedStatus(state, display, tenantName);
    }

    private static async Task<string> RunDsregcmdAsync(CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("dsregcmd.exe", "/status")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi)
                      ?? throw new InvalidOperationException("dsregcmd.exe could not be started.");
        var output = await p.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return output;
    }

    public enum JoinState { NotJoined, EntraJoined, HybridJoined, Registered }

    internal sealed record ParsedStatus(JoinState State, string StateDisplay, string? TenantName);
}
