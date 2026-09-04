using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

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

    /// <summary>
    /// Substrings (case-insensitive) of common VPN client adapters. Together
    /// with the PPP/Tunnel medium types these classify a connection as a VPN.
    /// </summary>
    private static readonly string[] VpnMarkers =
    {
        "VPN", "OpenVPN", "WireGuard", "AnyConnect", "Cisco AnyConnect",
        "GlobalProtect", "Palo Alto", "PANGP", "Pulse", "Ivanti",
        "FortiClient", "Fortinet", "NordVPN", "ExpressVPN", "Check Point",
        "Zscaler", "SonicWall", "NetExtender", "TAP-Windows", "WAN Miniport",
        "Juniper", "SoftEther",
    };

    /// <summary>Connection medium of the selected/enumerated adapter.</summary>
    public enum ConnectionKind { Ethernet, WiFi, Vpn }

    /// <summary>Lightweight, mockable view of a network adapter.</summary>
    public sealed record AdapterInfo(
        string Name,
        string Description,
        NetworkInterfaceType Type,
        bool IsUp,
        bool HasGateway,
        IReadOnlyList<string> IPv4Addresses,
        string Id = "",
        int InterfaceIndex = 0);

    /// <summary>The chosen adapter together with its usable (non-APIPA) IPv4.</summary>
    public sealed record AdapterSelection(AdapterInfo Adapter, string IPv4);

    /// <summary>
    /// An active adapter (Up, with a real non-APIPA IPv4) as shown in the
    /// network tile tooltip, classified by medium and flagged when it is the
    /// routing-active connection.
    /// </summary>
    public sealed record ActiveAdapter(AdapterInfo Adapter, string IPv4, ConnectionKind Kind, bool IsActive);

    /// <summary>
    /// Applies the adapter-selection rules and returns the best candidate, or
    /// <c>null</c> when no adapter offers real connectivity.
    /// </summary>
    public static AdapterSelection? SelectBest(IEnumerable<AdapterInfo> adapters) =>
        SelectBest(adapters, routeInterfaceIndex: null);

    /// <summary>
    /// Route-aware adapter selection. When <paramref name="routeInterfaceIndex"/>
    /// is supplied (from <see cref="GetRouteInterfaceIndex"/>, the native
    /// equivalent of <c>Find-NetRoute</c>) and maps to a candidate with a usable
    /// IPv4, that interface wins — this returns the correct IP for LAN, Wi-Fi,
    /// full- and split-tunnel VPNs and Hyper-V hosts alike. Otherwise it falls
    /// back to the gateway/physical priority heuristic.
    /// </summary>
    /// <remarks>
    /// Priority:
    /// <list type="number">
    /// <item>The interface the routing table would use to reach a public
    /// address (route-based, authoritative).</item>
    /// <item>A physical adapter (Ethernet/Wi-Fi) that owns a default gateway and
    /// does not match a virtual marker — the normal case.</item>
    /// <item>Any adapter that owns a default gateway (e.g. a Hyper-V External
    /// Switch that holds the gateway and therefore <em>is</em> the real link).</item>
    /// <item>Any remaining physical adapter that has a usable IPv4.</item>
    /// <item>Any remaining adapter with a usable IPv4.</item>
    /// </list>
    /// </remarks>
    public static AdapterSelection? SelectBest(IEnumerable<AdapterInfo> adapters, int? routeInterfaceIndex)
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

        // 0. Route-based: the interface Windows would actually use to reach a
        //    public address. Authoritative for VPNs (full- and split-tunnel),
        //    so it wins whenever it maps to a candidate with a usable IPv4.
        if (routeInterfaceIndex is int idx && idx > 0)
        {
            var routed = candidates.FirstOrDefault(s => s.Adapter.InterfaceIndex == idx);
            if (routed is not null)
                return routed;
        }

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
    /// Lists every active adapter (Up, with a real non-APIPA IPv4) classified by
    /// medium, with the routing-active connection flagged and placed first.
    /// Used to build the network tile's all-adapters tooltip.
    /// </summary>
    public static IReadOnlyList<ActiveAdapter> DescribeActiveAdapters(
        IEnumerable<AdapterInfo> adapters, int? routeInterfaceIndex)
    {
        var list = adapters as IList<AdapterInfo> ?? adapters.ToList();

        var candidates = list
            .Where(a => a.IsUp)
            .Select(a => new AdapterSelection(a, FirstUsableIPv4(a.IPv4Addresses)))
            .Where(s => s.IPv4.Length > 0)
            .ToList();

        if (candidates.Count == 0)
            return Array.Empty<ActiveAdapter>();

        var active = SelectBest(list, routeInterfaceIndex);

        var described = candidates.Select(s => new ActiveAdapter(
            s.Adapter,
            s.IPv4,
            Classify(s.Adapter),
            IsActive: active is not null &&
                      s.Adapter.InterfaceIndex == active.Adapter.InterfaceIndex &&
                      string.Equals(s.Adapter.Name, active.Adapter.Name, StringComparison.Ordinal) &&
                      s.IPv4 == active.IPv4));

        // Routing-active adapter first; the rest keep their enumeration order
        // (OrderBy is a stable sort).
        return described.OrderByDescending(a => a.IsActive).ToList();
    }

    /// <summary>Classifies an adapter's medium (VPN takes precedence over Wi-Fi/Ethernet).</summary>
    public static ConnectionKind Classify(AdapterInfo a)
    {
        if (IsVpn(a)) return ConnectionKind.Vpn;
        return a.Type == NetworkInterfaceType.Wireless80211
            ? ConnectionKind.WiFi
            : ConnectionKind.Ethernet;
    }

    /// <summary>
    /// True when the adapter is a VPN tunnel — either by medium
    /// (<see cref="NetworkInterfaceType.Tunnel"/>/<see cref="NetworkInterfaceType.Ppp"/>)
    /// or by a known VPN client name/description.
    /// </summary>
    public static bool IsVpn(AdapterInfo a) =>
        a.Type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp ||
        VpnMarkers.Any(m =>
            a.Name.Contains(m, StringComparison.OrdinalIgnoreCase) ||
            a.Description.Contains(m, StringComparison.OrdinalIgnoreCase));

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

            int ifIndex = 0;
            try { ifIndex = props.GetIPv4Properties()?.Index ?? 0; }
            catch { /* IPv4 disabled on this NIC - leave index 0 */ }

            result.Add(new AdapterInfo(
                Name: nic.Name,
                Description: nic.Description,
                Type: nic.NetworkInterfaceType,
                IsUp: nic.OperationalStatus == OperationalStatus.Up,
                HasGateway: hasGateway,
                IPv4Addresses: ipv4,
                Id: nic.Id,
                InterfaceIndex: ifIndex));
        }
        return result;
    }

    /// <summary>
    /// Returns the interface index the routing table would use to reach a public
    /// address (8.8.8.8), via the native <c>GetBestInterface</c> — the P/Invoke
    /// equivalent of PowerShell's <c>Find-NetRoute</c>. Only the local routing
    /// table is consulted; <b>no network traffic is sent</b>. Returns
    /// <c>null</c> when there is no route (offline) or the call fails.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static int? GetRouteInterfaceIndex()
    {
        try
        {
            // 8.8.8.8 as a DWORD in network byte order. All octets are equal so
            // endianness is irrelevant here.
            var bytes = System.Net.IPAddress.Parse("8.8.8.8").GetAddressBytes();
            uint dest = BitConverter.ToUInt32(bytes, 0);
            return GetBestInterface(dest, out uint index) == 0 ? (int)index : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the current SSID for the given NIC adapter GUID via the native
    /// <c>wlanapi.dll</c> WlanQueryInterface call. Returns <c>null</c> when the
    /// interface is not connected or the API is unavailable.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string? TryGetWifiSsid(string nicId)
    {
        try
        {
            if (!Guid.TryParse(nicId, out var guid)) return null;

            if (WlanOpenHandle(2, IntPtr.Zero, out _, out var client) != 0) return null;
            try
            {
                const uint OpcodeCurrentConnection = 7; // wlan_intf_opcode_current_connection
                if (WlanQueryInterface(client, ref guid, OpcodeCurrentConnection,
                        IntPtr.Zero, out _, out var dataPtr, out _) != 0)
                    return null;
                try
                {
                    // WLAN_CONNECTION_ATTRIBUTES layout (byte offsets):
                    //   isState            : [0]   4 bytes
                    //   wlanConnectionMode : [4]   4 bytes
                    //   strProfileName     : [8]   512 bytes  (256 WCHARs)
                    //   dot11Ssid.uSSIDLength: [520] 4 bytes
                    //   dot11Ssid.ucSSID   : [524] 32 bytes
                    const int SsidLengthOffset = 520;
                    const int SsidBytesOffset  = 524;
                    const int MaxSsidLength    = 32;

                    var length = Marshal.ReadInt32(dataPtr, SsidLengthOffset);
                    if (length <= 0 || length > MaxSsidLength) return null;

                    var ssidBytes = new byte[length];
                    Marshal.Copy(dataPtr + SsidBytesOffset, ssidBytes, 0, length);
                    return Encoding.UTF8.GetString(ssidBytes);
                }
                finally
                {
                    WlanFreeMemory(dataPtr);
                }
            }
            finally
            {
                WlanCloseHandle(client, IntPtr.Zero);
            }
        }
        catch
        {
            return null;
        }
    }

    #region Native P/Invoke
    // GetBestInterface only queries the local routing table; it sends nothing.
    [DllImport("iphlpapi.dll", SetLastError = false)]
    private static extern int GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);

    [DllImport("wlanapi.dll", SetLastError = false)]
    private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved,
        out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll", SetLastError = false)]
    private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [DllImport("wlanapi.dll", SetLastError = false)]
    private static extern void WlanFreeMemory(IntPtr pMemory);

    [DllImport("wlanapi.dll", SetLastError = false)]
    private static extern uint WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid,
        uint dwOpCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData,
        out uint pWlanOpcodeValueType);
    #endregion

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
