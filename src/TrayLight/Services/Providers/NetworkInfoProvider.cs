using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace TrayLight.Services.Providers;

/// <summary>
/// Active network connection: SSID for Wi-Fi, "Ethernet" otherwise, plus the
/// local IPv4. Icon switches between Wi-Fi and Ethernet glyphs.
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
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .OrderByDescending(n => n.Speed)
            .FirstOrDefault();

        if (nic is null) return new NetworkSnapshot(NetworkKind.Offline, string.Empty, string.Empty);

        var ipv4 = nic.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            ?.Address.ToString() ?? string.Empty;

        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        {
            // Best-effort SSID via netsh; falls back to the adapter name.
            var ssid = TryGetWifiSsid() ?? nic.Name;
            return new NetworkSnapshot(NetworkKind.WiFi, ssid, ipv4);
        }

        return new NetworkSnapshot(NetworkKind.Ethernet, "Ethernet", ipv4);
    }

    private static string? TryGetWifiSsid()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("netsh.exe", "wlan show interfaces")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(2000)) { try { p.Kill(); } catch { } return null; }

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                // Match "SSID  : Foo" but not "BSSID : ...". Look for the
                // first colon and check the key.
                var colon = trimmed.IndexOf(':');
                if (colon <= 0) continue;
                var key = trimmed[..colon].Trim();
                if (string.Equals(key, "SSID", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed[(colon + 1)..].Trim();
                    return string.IsNullOrEmpty(value) ? null : value;
                }
            }
        }
        catch
        {
            // netsh missing or access denied -> fall back to adapter name.
        }
        return null;
    }

    public enum NetworkKind { Offline, Ethernet, WiFi }

    public sealed record NetworkSnapshot(NetworkKind Kind, string DisplayName, string IPv4);
}
