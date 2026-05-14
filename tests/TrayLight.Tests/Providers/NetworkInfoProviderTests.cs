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
}
