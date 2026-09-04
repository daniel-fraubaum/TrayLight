using System.Runtime.Versioning;

namespace TrayLight.Services.Providers;

/// <summary>
/// Active network connection: SSID for Wi-Fi, "VPN" for tunnel/PPP adapters,
/// "Ethernet" otherwise, plus the IPv4 of the interface the routing table would
/// actually use to reach the internet (route-based, so a VPN IP wins over the
/// local LAN IP). Icon switches between Wi-Fi and Ethernet glyphs. Uses
/// <see cref="NetworkAdapterSelector"/> for route/adapter detection and the
/// native <c>wlanapi.dll</c> for the Wi-Fi SSID — no external processes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NetworkInfoProvider : InfoItemProviderBase
{
    public const string TypeKey = "networkInfo";

    private const string WifiIcon = "Segoe Fluent Icons:E701";     // Wifi
    private const string EthernetIcon = "Segoe Fluent Icons:EDA3"; // Ethernet
    private const string OfflineIcon = "Segoe Fluent Icons:E709";  // WifiError

    public override string Type => TypeKey;
    protected override string DefaultTitle => "Network";
    protected override string DefaultIcon => EthernetIcon;

    private readonly Func<NetworkSnapshot> _snapshotProvider;

    public NetworkInfoProvider() : this(BuildSnapshot) { }

    internal NetworkInfoProvider(Func<NetworkSnapshot> snapshotProvider)
    {
        _snapshotProvider = snapshotProvider;
    }

    protected override Task<InfoItemData> CollectAsync(CancellationToken cancellationToken)
    {
        var snap = _snapshotProvider();
        if (snap.Kind == NetworkKind.Offline)
        {
            // Offline status is shown on the tile but is not treated as a
            // warning - the OS already surfaces network failures clearly.
            return Task.FromResult(new InfoItemData(
                Title: EffectiveTitle,
                Value: "Offline",
                DetailText: "No active network connection.",
                HasWarning: false,
                WarningMessage: string.Empty,
                Icon: OfflineIcon));
        }

        var icon = snap.Kind == NetworkKind.WiFi ? WifiIcon : EthernetIcon;
        var ip = string.IsNullOrEmpty(snap.IPv4) ? "(no IPv4)" : snap.IPv4;
        return Task.FromResult(new InfoItemData(
            Title: EffectiveTitle,
            Value: snap.DisplayName,
            DetailText: ip,
            HasWarning: false,
            WarningMessage: string.Empty,
            Icon: Config?.Icon is { Length: > 0 } ? EffectiveIcon : icon));
    }

    protected override Task OnClickAsync(CancellationToken cancellationToken)
    {
        LaunchShell("ms-settings:network");
        return Task.CompletedTask;
    }

    private static NetworkSnapshot BuildSnapshot()
    {
        // Route-based selection: the IPv4 of the interface Windows would use to
        // reach the internet, so a full-tunnel VPN reports the VPN IP, a
        // split-tunnel reports the actually-routed link, and a Hyper-V host
        // reports its External-Switch address. See NetworkAdapterSelector.
        var summary = NetworkDisplay.Describe();

        if (!summary.Online)
            return new NetworkSnapshot(NetworkKind.Offline, string.Empty, string.Empty, string.Empty);

        var kind = summary.Kind switch
        {
            NetworkAdapterSelector.ConnectionKind.WiFi => NetworkKind.WiFi,
            NetworkAdapterSelector.ConnectionKind.Vpn  => NetworkKind.Vpn,
            _                                          => NetworkKind.Ethernet,
        };

        return new NetworkSnapshot(kind, summary.Label, summary.IPv4, summary.Tooltip);
    }

    public enum NetworkKind { Offline, Ethernet, WiFi, Vpn }

    public sealed record NetworkSnapshot(NetworkKind Kind, string DisplayName, string IPv4, string Tooltip = "");
}
