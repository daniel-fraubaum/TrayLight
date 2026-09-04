using System.Runtime.Versioning;
using TrayLight.Resources;

namespace TrayLight.Services.Providers;

/// <summary>
/// Builds the localized network tile display (value label + IPv4) and the
/// all-adapters hover tooltip from a route-based adapter query. Shared by the
/// popup tile and the <see cref="NetworkInfoProvider"/> so both show the same,
/// routing-active connection.
/// </summary>
public static class NetworkDisplay
{
    /// <summary>Localized network display: value label, active IPv4 and the tooltip listing every active adapter.</summary>
    public sealed record Summary(
        bool Online,
        NetworkAdapterSelector.ConnectionKind Kind,
        string Label,
        string IPv4,
        string Tooltip);

    /// <summary>
    /// Queries live adapters, picks the routing-active one and formats the
    /// display + tooltip. Returns an offline summary when nothing has a usable
    /// non-APIPA IPv4.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static Summary Describe()
    {
        var adapters = NetworkAdapterSelector.EnumerateLiveAdapters();
        var routeIndex = NetworkAdapterSelector.GetRouteInterfaceIndex();
        var active = NetworkAdapterSelector.DescribeActiveAdapters(adapters, routeIndex);

        if (active.Count == 0)
            return new Summary(false, NetworkAdapterSelector.ConnectionKind.Ethernet,
                string.Empty, string.Empty, string.Empty);

        var primary = active[0];
        return new Summary(
            Online: true,
            Kind: primary.Kind,
            Label: Label(primary, includeSsid: true),
            IPv4: primary.IPv4,
            Tooltip: BuildTooltip(active));
    }

    /// <summary>
    /// One line per active adapter ("WiFi CorpNet: 10.0.0.4 (active)"), the
    /// routing-active one first and flagged with the localized "(active)" marker.
    /// Wireless entries include the SSID. <paramref name="ssidResolver"/> is a
    /// test seam; production uses the native lookup.
    /// </summary>
    public static string BuildTooltip(
        IReadOnlyList<NetworkAdapterSelector.ActiveAdapter> active,
        Func<string, string?>? ssidResolver = null) =>
        string.Join("\n", active.Select(a =>
        {
            var line = $"{Label(a, includeSsid: true, ssidResolver)}: {a.IPv4}";
            return a.IsActive ? $"{line} ({Strings.NetworkActiveMarker})" : line;
        }));

    /// <summary>
    /// Localized medium label: "VPN", "WiFi {SSID}" (SSID only when
    /// <paramref name="includeSsid"/>) or "Ethernet".
    /// </summary>
    public static string Label(
        NetworkAdapterSelector.ActiveAdapter a,
        bool includeSsid,
        Func<string, string?>? ssidResolver = null) => a.Kind switch
    {
        NetworkAdapterSelector.ConnectionKind.Vpn  => Strings.NetworkVpn,
        NetworkAdapterSelector.ConnectionKind.WiFi => includeSsid ? WifiLabel(a.Adapter.Id, ssidResolver) : Strings.NetworkWifi,
        _                                          => Strings.NetworkEthernet,
    };

    private static string WifiLabel(string nicId, Func<string, string?>? ssidResolver)
    {
        var ssid = (ssidResolver ?? NetworkAdapterSelector.TryGetWifiSsid)(nicId);
        return string.IsNullOrEmpty(ssid) ? Strings.NetworkWifi : $"{Strings.NetworkWifi} {ssid}";
    }
}
