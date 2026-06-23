using System.Runtime.Versioning;
using TrayLight.Services.Providers;
using TrayLight.ViewModels;

namespace TrayLight.Services.Actions;

/// <summary>
/// Production <see cref="IShortcutPlaceholderResolver"/>. Provider-backed tokens
/// (<c>ComputerName</c>, <c>OsVersion</c>, <c>LastReboot</c>, <c>Storage</c>,
/// <c>Network</c>) are read from the live <see cref="IInfoItemProvider"/>
/// singletons; the remaining tokens (<c>SerialNumber</c>, <c>IntuneSync</c>,
/// <c>UserName</c>, <c>DomainName</c>) are computed directly. Every resolver
/// is wrapped so a failure yields a <c>N/A</c> fallback rather than crashing
/// the click handler.
/// </summary>
public sealed class ShortcutPlaceholderResolver : IShortcutPlaceholderResolver
{
    private readonly IReadOnlyDictionary<string, IInfoItemProvider> _providers;
    private readonly Func<string> _userName;
    private readonly Func<string> _domainName;
    private readonly Func<string> _serialNumber;
    private readonly Func<string> _intuneSync;

    public ShortcutPlaceholderResolver(IEnumerable<IInfoItemProvider> providers)
        : this(providers, null, null, null, null) { }

    /// <summary>Test-friendly constructor: every direct value source is injectable.</summary>
    internal ShortcutPlaceholderResolver(
        IEnumerable<IInfoItemProvider> providers,
        Func<string>? userName,
        Func<string>? domainName,
        Func<string>? serialNumber,
        Func<string>? intuneSync)
    {
        _providers = (providers ?? Enumerable.Empty<IInfoItemProvider>())
            .GroupBy(p => p.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        _userName     = userName     ?? (() => Environment.UserName);
        _domainName   = domainName   ?? DefaultDomainName;
        _serialNumber = serialNumber ?? DefaultSerialNumber;
        _intuneSync   = intuneSync   ?? DefaultIntuneSync;
    }

    public async Task<string> ExpandAsync(string action, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(action) || !ShortcutPlaceholders.ContainsTokens(action))
            return action ?? string.Empty;

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in ShortcutPlaceholders.ExtractTokens(action).Distinct(StringComparer.OrdinalIgnoreCase))
            values[token] = await ResolveTokenAsync(token, cancellationToken).ConfigureAwait(false);

        return ShortcutPlaceholders.Expand(action, values);
    }

    private async Task<string?> ResolveTokenAsync(string token, CancellationToken ct)
    {
        try
        {
            return token.ToLowerInvariant() switch
            {
                "computername" => await ProviderValueAsync(ComputerNameProvider.TypeKey, ct).ConfigureAwait(false),
                "osversion"    => await ProviderValueAsync(OsVersionProvider.TypeKey, ct).ConfigureAwait(false),
                "lastreboot"   => await ProviderValueAsync(LastRebootProvider.TypeKey, ct).ConfigureAwait(false),
                "storage"      => await ProviderValueAsync(StorageUsedProvider.TypeKey, ct).ConfigureAwait(false),
                "network"      => await NetworkValueAsync(ct).ConfigureAwait(false),
                "username"     => Safe(_userName),
                "domainname"   => Safe(_domainName),
                "serialnumber" => Safe(_serialNumber),
                "intunesync"   => Safe(_intuneSync),
                _              => null, // unknown token -> engine substitutes N/A
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ProviderValueAsync(string typeKey, CancellationToken ct)
    {
        if (!_providers.TryGetValue(typeKey, out var provider)) return null;
        var data = await provider.GetDataAsync(ct).ConfigureAwait(false);
        return data.Value;
    }

    private async Task<string?> NetworkValueAsync(CancellationToken ct)
    {
        if (!_providers.TryGetValue(NetworkInfoProvider.TypeKey, out var provider)) return null;
        var data = await provider.GetDataAsync(ct).ConfigureAwait(false);

        // The Network tile splits "type" (Value, e.g. "Ethernet") and the IP
        // (DetailText). The placeholder combines both, e.g. "Ethernet 192.168.0.5".
        var detail = data.DetailText;
        if (string.IsNullOrWhiteSpace(detail) ||
            detail.StartsWith("(", StringComparison.Ordinal) ||
            string.Equals(detail, data.Value, StringComparison.OrdinalIgnoreCase))
            return data.Value;

        return $"{data.Value} {detail}".Trim();
    }

    private static string? Safe(Func<string> source)
    {
        try
        {
            var value = source();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static string DefaultDomainName()
    {
        // Environment.UserDomainName returns the AD domain when joined and the
        // machine (workgroup) name otherwise — which matches the spec's
        // "domain or workgroup name".
        var domain = Environment.UserDomainName;
        return string.IsNullOrWhiteSpace(domain) ? Environment.MachineName : domain;
    }

    [SupportedOSPlatform("windows")]
    private static string DefaultIntuneSync()
    {
        var status = IntuneSyncProvider.ReadStatus();
        if (!status.IsEnrolled) return "Not enrolled";
        if (status.LastSyncUtc is { } sync)
            return TrayPopupViewModel.FormatRelative(DateTime.UtcNow - sync);
        return "Unknown";
    }

    [SupportedOSPlatform("windows")]
    private static string DefaultSerialNumber()
    {
        // BIOS first, then chassis / motherboard (VMs often leave BIOS empty).
        foreach (var (cls, prop) in new[]
        {
            ("Win32_BIOS",            "SerialNumber"),
            ("Win32_SystemEnclosure", "SerialNumber"),
            ("Win32_BaseBoard",       "SerialNumber"),
        })
        {
            var s = QueryWmiString(cls, prop);
            if (IsValidSerial(s)) return s!.Trim();
        }
        return ShortcutPlaceholders.Unresolved;
    }

    private static bool IsValidSerial(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        return !t.Equals("None",                   StringComparison.OrdinalIgnoreCase) &&
               !t.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) &&
               !t.Equals("Default string",         StringComparison.OrdinalIgnoreCase) &&
               !t.Equals("System Serial Number",   StringComparison.OrdinalIgnoreCase) &&
               !t.Equals("0",                      StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    private static string? QueryWmiString(string wmiClass, string property)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT {property} FROM {wmiClass}");
            foreach (var mo in searcher.Get())
            {
                using (mo)
                {
                    var val = mo[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
            }
        }
        catch { /* WMI unavailable */ }
        return null;
    }
}
