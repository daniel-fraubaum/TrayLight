using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace TrayLight.Services.Providers;

/// <summary>
/// Active network connection: SSID for Wi-Fi, "Ethernet" otherwise, plus the
/// local IPv4. Icon switches between Wi-Fi and Ethernet glyphs.
/// Uses <see cref="NetworkInterface"/> for adapter enumeration and the native
/// <c>wlanapi.dll</c> for Wi-Fi SSID — no external processes.
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
            // Try to get the SSID via the native WlanAPI. Falls back to the
            // adapter name when the Wi-Fi subsystem is unavailable.
            var ssid = TryGetWifiSsidNative(nic.Id) ?? nic.Name;
            return new NetworkSnapshot(NetworkKind.WiFi, ssid, ipv4);
        }

        return new NetworkSnapshot(NetworkKind.Ethernet, "Ethernet", ipv4);
    }

    /// <summary>
    /// Reads the current SSID for the given NIC adapter GUID via the native
    /// <c>wlanapi.dll</c> WlanQueryInterface call. Returns <c>null</c> when the
    /// interface is not connected or the API is unavailable.
    /// </summary>
    private static string? TryGetWifiSsidNative(string nicId)
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

    #region WlanAPI P/Invoke
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

    public enum NetworkKind { Offline, Ethernet, WiFi }

    public sealed record NetworkSnapshot(NetworkKind Kind, string DisplayName, string IPv4);
}
