using TrayLight.Models;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Providers;

public class NetworkInfoProviderTests
{
    [Fact]
    public async Task Wifi_snapshot_uses_wifi_glyph_and_ssid()
    {
        var sut = new NetworkInfoProvider(() => new NetworkInfoProvider.NetworkSnapshot(
            NetworkInfoProvider.NetworkKind.WiFi, "Contoso-Guest", "10.0.0.42"));
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal("Contoso-Guest", data.Value);
        Assert.Equal("10.0.0.42", data.DetailText);
        Assert.Contains("E701", data.Icon);
        Assert.False(data.HasWarning);
    }

    [Fact]
    public async Task Ethernet_snapshot_uses_ethernet_glyph()
    {
        var sut = new NetworkInfoProvider(() => new NetworkInfoProvider.NetworkSnapshot(
            NetworkInfoProvider.NetworkKind.Ethernet, "Ethernet", "192.168.1.5"));
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal("Ethernet", data.Value);
        Assert.Contains("EDA3", data.Icon);
    }

    [Fact]
    public async Task Offline_does_not_warn_and_uses_offline_glyph()
    {
        var sut = new NetworkInfoProvider(() => new NetworkInfoProvider.NetworkSnapshot(
            NetworkInfoProvider.NetworkKind.Offline, string.Empty, string.Empty));
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal("Offline", data.Value);
        Assert.False(data.HasWarning);
        Assert.Contains("E709", data.Icon);
    }

    [Fact]
    public async Task Configured_icon_overrides_kind_specific_glyph()
    {
        var sut = new NetworkInfoProvider(() => new NetworkInfoProvider.NetworkSnapshot(
            NetworkInfoProvider.NetworkKind.WiFi, "X", "1.2.3.4"));
        sut.Configure(new InfoItemConfig { Icon = "Segoe Fluent Icons:E946" });

        var data = await sut.GetDataAsync();

        Assert.Equal("Segoe Fluent Icons:E946", data.Icon);
    }

    [Fact]
    public void Tooltip_lists_all_active_adapters_with_the_active_marker_first()
    {
        var original = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture =
                System.Globalization.CultureInfo.GetCultureInfo("en-US");

            var eth = new NetworkAdapterSelector.ActiveAdapter(
                new NetworkAdapterSelector.AdapterInfo("Ethernet", "Intel NIC",
                    System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,
                    IsUp: true, HasGateway: true, IPv4Addresses: new[] { "192.168.170.131" },
                    Id: "", InterfaceIndex: 11),
                "192.168.170.131", NetworkAdapterSelector.ConnectionKind.Ethernet, IsActive: true);

            var vpn = new NetworkAdapterSelector.ActiveAdapter(
                new NetworkAdapterSelector.AdapterInfo("Corp VPN", "PPP",
                    System.Net.NetworkInformation.NetworkInterfaceType.Ppp,
                    IsUp: true, HasGateway: false, IPv4Addresses: new[] { "195.169.220.167" },
                    Id: "", InterfaceIndex: 22),
                "195.169.220.167", NetworkAdapterSelector.ConnectionKind.Vpn, IsActive: false);

            var tooltip = NetworkDisplay.BuildTooltip(new[] { eth, vpn });

            Assert.Equal(
                "Ethernet: 192.168.170.131 (active)\nVPN: 195.169.220.167",
                tooltip);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Tooltip_appends_ssid_to_wireless_entries()
    {
        var original = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture =
                System.Globalization.CultureInfo.GetCultureInfo("en-US");

            var wifi = new NetworkAdapterSelector.ActiveAdapter(
                new NetworkAdapterSelector.AdapterInfo("Wi-Fi", "Intel Wi-Fi 6",
                    System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211,
                    IsUp: true, HasGateway: true, IPv4Addresses: new[] { "10.0.0.4" },
                    Id: "wifi-guid", InterfaceIndex: 5),
                "10.0.0.4", NetworkAdapterSelector.ConnectionKind.WiFi, IsActive: true);

            var eth = new NetworkAdapterSelector.ActiveAdapter(
                new NetworkAdapterSelector.AdapterInfo("Ethernet", "Intel NIC",
                    System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,
                    IsUp: true, HasGateway: true, IPv4Addresses: new[] { "192.168.1.50" },
                    Id: "", InterfaceIndex: 6),
                "192.168.1.50", NetworkAdapterSelector.ConnectionKind.Ethernet, IsActive: false);

            // Inject the SSID resolver so the test doesn't depend on live Wi-Fi.
            var tooltip = NetworkDisplay.BuildTooltip(new[] { wifi, eth }, ssidResolver: _ => "CorpNet");

            Assert.Equal(
                "WiFi CorpNet: 10.0.0.4 (active)\nEthernet: 192.168.1.50",
                tooltip);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = original;
        }
    }
}
