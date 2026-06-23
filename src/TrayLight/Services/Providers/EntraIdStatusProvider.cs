using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TrayLight.Services.Providers;

/// <summary>
/// Reports Entra ID (Azure AD) join state by reading registry keys directly.
/// Replaces the previous dsregcmd.exe approach which was fragile, slow (~200 ms)
/// and could be blocked by ASR rules.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EntraIdStatusProvider : InfoItemProviderBase
{
    public const string TypeKey = "entraIdStatus";

    public override string Type => TypeKey;
    protected override string DefaultTitle => "Identity";
    protected override string DefaultIcon => "Segoe Fluent Icons:E910"; // Contact

    private readonly Func<ParsedStatus> _statusReader;

    public EntraIdStatusProvider() : this(ReadFromRegistry) { }

    internal EntraIdStatusProvider(Func<ParsedStatus> statusReader)
    {
        _statusReader = statusReader;
    }

    protected override Task<InfoItemData> CollectAsync(CancellationToken cancellationToken)
    {
        var parsed = _statusReader();

        return Task.FromResult(new InfoItemData(
            Title: EffectiveTitle,
            Value: parsed.StateDisplay,
            DetailText: string.IsNullOrEmpty(parsed.TenantName)
                ? string.Empty
                : $"Tenant: {parsed.TenantName}",
            // Identity tile is informational - join state is shown but never
            // surfaced as a warning.
            HasWarning: false,
            WarningMessage: string.Empty,
            Icon: EffectiveIcon));
    }

    protected override Task OnClickAsync(CancellationToken cancellationToken)
    {
        LaunchShell("ms-settings:workplace");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads Entra ID / domain join state directly from the Windows registry.
    /// Equivalent to parsing <c>dsregcmd /status</c> but ~100× faster and not
    /// subject to ASR rules that block process creation.
    /// </summary>
    internal static ParsedStatus ReadFromRegistry()
    {
        bool azureAdJoined = false, domainJoined = false, workplaceJoined = false;
        string? tenantName = null;

        // Azure AD / Entra ID joined (device-level): the JoinInfo subkey tree
        // contains one entry per join, each identified by a device-ID GUID.
        try
        {
            using var joinInfo = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
            if (joinInfo != null)
            {
                var deviceKeys = joinInfo.GetSubKeyNames();
                if (deviceKeys.Length > 0)
                {
                    azureAdJoined = true;
                    using var deviceKey = joinInfo.OpenSubKey(deviceKeys[0]);
                    tenantName = deviceKey?.GetValue("TenantName") as string;
                }
            }
        }
        catch { }

        // AD domain join: Netlogon stores the FQDN when the device is joined.
        try
        {
            using var netlogon = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\Netlogon\Parameters");
            var domain = netlogon?.GetValue("Domain") as string;
            domainJoined = !string.IsNullOrWhiteSpace(domain);
        }
        catch { }

        // Workplace registered (user-level Azure AD registration, not full join):
        // Windows writes HKCU join info for registered-but-not-joined devices.
        try
        {
            using var hkcuJoinInfo = Registry.CurrentUser.OpenSubKey(
                @"System\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
            workplaceJoined = hkcuJoinInfo?.GetSubKeyNames().Length > 0;
        }
        catch { }

        var (state, display) = (azureAdJoined, domainJoined, workplaceJoined) switch
        {
            (true, true, _)  => (JoinState.HybridJoined, "Hybrid Joined"),
            (true, false, _) => (JoinState.EntraJoined,  "Entra ID Joined"),
            (false, _, true) => (JoinState.Registered,   "Registered"),
            _                => (JoinState.NotJoined,    "Not Joined")
        };

        return new ParsedStatus(state, display, tenantName);
    }

    /// <summary>
    /// Parses raw <c>dsregcmd /status</c> output into a <see cref="ParsedStatus"/>.
    /// Kept for backward compatibility with tests; production code now uses
    /// <see cref="ReadFromRegistry"/> instead.
    /// </summary>
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

    public enum JoinState { NotJoined, EntraJoined, HybridJoined, Registered }

    internal sealed record ParsedStatus(JoinState State, string StateDisplay, string? TenantName);
}
