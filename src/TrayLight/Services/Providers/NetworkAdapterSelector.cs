using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace TrayLight.Services.Providers;

/// <summary>
/// Picks the "real" network adapter and its usable IPv4 address using a
/// priority approach rather than a hard name-based exclusion list.
///
/// APIPA (169.254.x.x) and loopback (127.x) addresses are the only things that
/// are hard-excluded, because they never represent real connectivity. Virtual
/// adapter names are only used to <em>de-prioritise</em> a candidate, never to
/// remove it outright — otherwise a Hyper-V host whose active physical
/// connection runs through a "vEthernet" External Switch would be reported as
/// offline even though it is online.
///
/// The selection logic is separated from live <see cref="NetworkInterface"/>
/// enumeration so it can be unit-tested with mocked adapter lists.
/// </summary>
public static class NetworkAdapterSelector
{
    /// <summary>
    /// Substrings (case-insensitive) that identify a virtual / non-physical
    /// adapter. A match on the adapter name or description only lowers the
    /// candidate's priority; it is never used to exclude an adapter outright.
    /// </summary>
    private static readonly string[] VirtualMarkers =
    {
        "Hyper-V", "VMware", "VirtualBox", "vEthernet", "WSL",
        "TAP", "Loopback",
    };

    /// <summary>Lightweight, mockable view of a network adapter.</summary>
    public sealed record AdapterInfo(
        string Name,
        string Description,
        NetworkInterfaceType Type,
        bool IsUp,
        bool HasGateway,
        IReadOnlyList<string> IPv4Addresses,
        string Id = "");

    /// <summary>The chosen adapter together with its usable (non-APIPA) IPv4.</summary>
    public sealed record AdapterSelection(AdapterInfo Adapter, string IPv4);

    /// <summary>
    /// Applies the adapter-selection rules and returns the best candidate, or
    /// <c>null</c> when no adapter offers real connectivity.
    /// </summary>
    /// <remarks>
    /// Priority:
    /// <list type="number">
    /// <item>A physical adapter (Ethernet/Wi-Fi) that owns a default gateway and
    /// does not match a virtual marker — the normal case.</item>
    /// <item>Any adapter that owns a default gateway. This catches the Hyper-V
    /// External Switch, where the active connection runs through a "vEthernet"
    /// adapter that holds the gateway and therefore <em>is</em> the real link.</item>
    /// <item>Any remaining physical adapter that has a usable IPv4.</item>
    /// <item>Any remaining adapter with a usable IPv4, so "offline" is only
    /// reported when nothing has a valid non-APIPA/non-loopback IPv4 at all.</item>
    /// </list>
    /// </remarks>
    public static AdapterSelection? SelectBest(IEnumerable<AdapterInfo> adapters)
    {
        // 1. Only adapters that are Up, keeping their first usable IPv4.
        // 2. Hard-exclude ONLY APIPA (169.254.x.x) and loopback (127.x)
        //    addresses — those never represent real connectivity.
        var candidates = adapters
            .Where(a => a.IsUp)
            .Select(a => new AdapterSelection(a, FirstUsableIPv4(a.IPv4Addresses)))
            .Where(s => s.IPv4.Length > 0)
            .ToList();

        if (candidates.Count == 0)
            return null;

        // 3a. First choice: physical type, owns a gateway, not a virtual name.
        var physicalWithGateway = candidates.FirstOrDefault(s =>
            s.Adapter.HasGateway &&
            IsPhysical(s.Adapter.Type) &&
            !IsVirtual(s.Adapter));
        if (physicalWithGateway is not null)
            return physicalWithGateway;

        // 3b. Second choice: ANY adapter that owns the default gateway. On a
        //     Hyper-V host with an External Switch the active physical
        //     connection runs through a "vEthernet" adapter — if it holds the
        //     gateway it IS the real connection, so it must NOT be excluded.
        var anyWithGateway = candidates.FirstOrDefault(s => s.Adapter.HasGateway);
        if (anyWithGateway is not null)
            return anyWithGateway;

        // 3c. Third choice: any remaining physical adapter with an IPv4.
        var physical = candidates.FirstOrDefault(s => IsPhysical(s.Adapter.Type));
        if (physical is not null)
            return physical;

        // 4. Last resort: the first adapter with a usable IPv4, so we only fall
        //    through to "offline" when nothing has a valid IPv4.
        return candidates[0];
    }

    /// <summary>
    /// Live-enumerates the machine's network interfaces into
    /// <see cref="AdapterInfo"/> records suitable for <see cref="SelectBest"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<AdapterInfo> EnumerateLiveAdapters()
    {
        var result = new List<AdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); }
            catch { continue; }

            var ipv4 = props.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .ToList();

            var hasGateway = props.GatewayAddresses
                .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                          !g.Address.Equals(System.Net.IPAddress.Any));

            result.Add(new AdapterInfo(
                Name: nic.Name,
                Description: nic.Description,
                Type: nic.NetworkInterfaceType,
                IsUp: nic.OperationalStatus == OperationalStatus.Up,
                HasGateway: hasGateway,
                IPv4Addresses: ipv4,
                Id: nic.Id));
        }
        return result;
    }

    private static bool IsVirtual(AdapterInfo a) =>
        VirtualMarkers.Any(marker =>
            a.Name.Contains(marker, StringComparison.OrdinalIgnoreCase) ||
            a.Description.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsPhysical(NetworkInterfaceType type) =>
        type is NetworkInterfaceType.Ethernet
             or NetworkInterfaceType.GigabitEthernet
             or NetworkInterfaceType.FastEthernetT
             or NetworkInterfaceType.FastEthernetFx
             or NetworkInterfaceType.Wireless80211;

    private static string FirstUsableIPv4(IReadOnlyList<string> addresses) =>
        addresses.FirstOrDefault(ip =>
            ip.Length > 0 &&
            !ip.StartsWith("169.254.", StringComparison.Ordinal) &&
            !ip.StartsWith("127.", StringComparison.Ordinal)) ?? string.Empty;
}
