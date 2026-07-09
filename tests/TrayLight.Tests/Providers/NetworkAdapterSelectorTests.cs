using System.Net.NetworkInformation;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Providers;

public class NetworkAdapterSelectorTests
{
    private static NetworkAdapterSelector.AdapterInfo Adapter(
        string name,
        string description,
        NetworkInterfaceType type = NetworkInterfaceType.Ethernet,
        bool isUp = true,
        bool hasGateway = true,
        params string[] ipv4)
        => new(name, description, type, isUp, hasGateway, ipv4);

    [Fact]
    public void Prefers_physical_gateway_adapter_over_gatewayless_HyperV_switch()
    {
        var adapters = new[]
        {
            Adapter("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter",
                hasGateway: false, ipv4: "192.168.160.1"),
            Adapter("Ethernet", "Intel(R) Ethernet Connection I219-LM",
                hasGateway: true, ipv4: "10.0.0.50"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.NotNull(result);
        Assert.Equal("Ethernet", result!.Adapter.Name);
        Assert.Equal("10.0.0.50", result.IPv4);
    }

    [Fact]
    public void Prefers_physical_gateway_adapter_over_gatewayless_VMware_adapter()
    {
        var adapters = new[]
        {
            Adapter("VMware Network Adapter VMnet8", "VMware Virtual Ethernet Adapter for VMnet8",
                hasGateway: false, ipv4: "192.168.220.1"),
            Adapter("Wi-Fi", "Intel(R) Wi-Fi 6 AX201 160MHz",
                type: NetworkInterfaceType.Wireless80211, hasGateway: true, ipv4: "172.16.4.20"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.NotNull(result);
        Assert.Equal("Wi-Fi", result!.Adapter.Name);
        Assert.Equal("172.16.4.20", result.IPv4);
    }

    [Fact]
    public void Apipa_only_adapter_is_treated_as_no_connection()
    {
        var adapters = new[]
        {
            Adapter("Ethernet", "Realtek PCIe GbE Family Controller",
                hasGateway: false, ipv4: "169.254.225.2"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.Null(result);
    }

    [Fact]
    public void Prefers_adapter_with_gateway_over_one_without()
    {
        var adapters = new[]
        {
            Adapter("Ethernet 2", "Secondary NIC (no gateway)",
                hasGateway: false, ipv4: "10.1.1.5"),
            Adapter("Ethernet", "Primary NIC (gateway)",
                hasGateway: true, ipv4: "10.0.0.10"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.NotNull(result);
        Assert.Equal("Ethernet", result!.Adapter.Name);
        Assert.Equal("10.0.0.10", result.IPv4);
    }

    [Fact]
    public void Falls_back_to_physical_adapter_without_gateway_when_none_have_one()
    {
        var adapters = new[]
        {
            Adapter("Ethernet", "Physical NIC",
                type: NetworkInterfaceType.Ethernet, hasGateway: false, ipv4: "10.0.0.30"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.NotNull(result);
        Assert.Equal("Ethernet", result!.Adapter.Name);
    }

    [Fact]
    public void Prefers_physical_type_when_gateway_state_is_equal()
    {
        var adapters = new[]
        {
            Adapter("Tunnel", "Some tunnel adapter",
                type: NetworkInterfaceType.Ppp, hasGateway: true, ipv4: "10.9.9.9"),
            Adapter("Ethernet", "Intel NIC",
                type: NetworkInterfaceType.Ethernet, hasGateway: true, ipv4: "10.0.0.7"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.NotNull(result);
        Assert.Equal("Ethernet", result!.Adapter.Name);
    }

    [Fact]
    public void Skips_apipa_address_but_uses_a_real_ipv4_on_the_same_adapter()
    {
        var adapters = new[]
        {
            Adapter("Ethernet", "Intel NIC",
                hasGateway: true, ipv4: new[] { "169.254.10.10", "192.168.1.42" }),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.NotNull(result);
        Assert.Equal("192.168.1.42", result!.IPv4);
    }

    [Fact]
    public void Ignores_adapters_that_are_down()
    {
        var adapters = new[]
        {
            Adapter("Ethernet", "Intel NIC", isUp: false, hasGateway: true, ipv4: "10.0.0.99"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.Null(result);
    }

    [Fact]
    public void HyperV_external_switch_is_selected_when_it_is_the_only_gateway_adapter()
    {
        // On a Hyper-V host with an External Switch the physical NIC is bridged
        // and loses its gateway; the active connection runs through the
        // "vEthernet" adapter. It must be selected, never filtered by name.
        var adapters = new[]
        {
            Adapter("Ethernet", "Intel(R) Ethernet Connection I219-LM",
                type: NetworkInterfaceType.Ethernet, hasGateway: false, ipv4: "10.0.0.51"),
            Adapter("vEthernet (External Switch)", "Hyper-V Virtual Ethernet Adapter",
                type: NetworkInterfaceType.Ethernet, hasGateway: true, ipv4: "10.0.0.50"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.NotNull(result);
        Assert.Equal("vEthernet (External Switch)", result!.Adapter.Name);
        Assert.Equal("10.0.0.50", result.IPv4);
    }

    [Fact]
    public void Physical_adapter_wins_over_virtual_when_both_have_a_gateway()
    {
        var adapters = new[]
        {
            Adapter("vEthernet (External Switch)", "Hyper-V Virtual Ethernet Adapter",
                type: NetworkInterfaceType.Ethernet, hasGateway: true, ipv4: "172.16.0.5"),
            Adapter("Ethernet", "Intel(R) Ethernet Connection I219-LM",
                type: NetworkInterfaceType.Ethernet, hasGateway: true, ipv4: "10.0.0.60"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.NotNull(result);
        Assert.Equal("Ethernet", result!.Adapter.Name);
        Assert.Equal("10.0.0.60", result.IPv4);
    }

    [Fact]
    public void Apipa_only_across_all_adapters_reports_no_connection()
    {
        var adapters = new[]
        {
            Adapter("Ethernet", "Realtek PCIe GbE Family Controller",
                hasGateway: false, ipv4: "169.254.10.20"),
            Adapter("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter",
                hasGateway: false, ipv4: "169.254.225.2"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.Null(result);
    }

    [Fact]
    public void Reports_no_connection_when_only_apipa_virtual_adapters_present()
    {
        // Virtual adapters are no longer excluded by name, so "offline" is only
        // reported when nothing has a valid non-APIPA IPv4 at all.
        var adapters = new[]
        {
            Adapter("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter",
                hasGateway: false, ipv4: "169.254.20.1"),
            Adapter("VirtualBox Host-Only Network", "VirtualBox Host-Only Ethernet Adapter",
                hasGateway: false, ipv4: "169.254.56.1"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.Null(result);
    }

    [Fact]
    public void Longest_ipv4_address_is_returned_intact()
    {
        var adapters = new[]
        {
            Adapter("Ethernet", "Intel NIC", hasGateway: true, ipv4: "255.255.255.255"),
        };

        var result = NetworkAdapterSelector.SelectBest(adapters);

        Assert.NotNull(result);
        Assert.Equal("255.255.255.255", result!.IPv4);
        Assert.Equal(15, result.IPv4.Length);
    }
}
