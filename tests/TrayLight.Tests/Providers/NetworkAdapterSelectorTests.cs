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

    // ---- Route-based active-IP detection (issue #17) ----------------------

    private static NetworkAdapterSelector.AdapterInfo Ax(
        string name,
        NetworkInterfaceType type,
        bool hasGateway,
        int interfaceIndex,
        string ip,
        string description = "")
        => new(name, description.Length == 0 ? name : description, type,
               IsUp: true, hasGateway, new[] { ip }, Id: "", InterfaceIndex: interfaceIndex);

    [Fact]
    public void Route_selects_lan_when_it_is_the_only_link()
    {
        var lan = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: true, 11, "192.168.1.50");

        var result = NetworkAdapterSelector.SelectBest(new[] { lan }, routeInterfaceIndex: 11);

        Assert.NotNull(result);
        Assert.Equal("192.168.1.50", result!.IPv4);
    }

    [Fact]
    public void Full_tunnel_vpn_ip_wins_over_lan_via_route()
    {
        // The physical LAN owns the gateway and is physical, so the old
        // heuristic would pick it. The route says traffic exits via the VPN, so
        // route-based selection must return the VPN IP instead.
        var lan = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: true, 11, "192.168.1.50");
        var vpn = Ax("Corp VPN", NetworkInterfaceType.Ppp, hasGateway: false, 22, "10.8.0.2");

        var routed = NetworkAdapterSelector.SelectBest(new[] { lan, vpn }, routeInterfaceIndex: 22);
        var heuristic = NetworkAdapterSelector.SelectBest(new[] { lan, vpn });

        Assert.Equal("10.8.0.2", routed!.IPv4);     // VPN wins on the active route
        Assert.Equal("192.168.1.50", heuristic!.IPv4); // fallback still prefers physical gateway
    }

    [Fact]
    public void Split_tunnel_uses_the_physical_link_the_route_points_to()
    {
        // Split tunnel: internet-bound traffic (8.8.8.8) exits the ISP link, so
        // GetBestInterface returns the Ethernet index, not the VPN.
        var lan = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: true, 11, "192.168.1.50");
        var vpn = Ax("Corp VPN", NetworkInterfaceType.Ppp, hasGateway: false, 22, "10.8.0.2");

        var result = NetworkAdapterSelector.SelectBest(new[] { lan, vpn }, routeInterfaceIndex: 11);

        Assert.Equal("192.168.1.50", result!.IPv4);
    }

    [Fact]
    public void Hyperv_host_returns_external_switch_ip_via_route()
    {
        var phys = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: false, 11, "10.0.0.51");
        var vsw  = Ax("vEthernet (External Switch)", NetworkInterfaceType.Ethernet, hasGateway: true, 22,
            "10.0.0.50", "Hyper-V Virtual Ethernet Adapter");

        var result = NetworkAdapterSelector.SelectBest(new[] { phys, vsw }, routeInterfaceIndex: 22);

        Assert.Equal("10.0.0.50", result!.IPv4);
    }

    [Fact]
    public void Vm_guest_single_adapter_is_returned_by_route()
    {
        var nic = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: true, 7, "192.168.100.5");

        var result = NetworkAdapterSelector.SelectBest(new[] { nic }, routeInterfaceIndex: 7);

        Assert.Equal("192.168.100.5", result!.IPv4);
    }

    [Fact]
    public void No_connectivity_returns_null_even_with_a_route_index()
    {
        // Only an APIPA address exists; the route index maps to it but it is not
        // a usable IPv4, so selection falls through to "offline".
        var apipa = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: false, 11, "169.254.1.1");

        Assert.Null(NetworkAdapterSelector.SelectBest(new[] { apipa }, routeInterfaceIndex: 11));
        Assert.Null(NetworkAdapterSelector.SelectBest(new[] { apipa }, routeInterfaceIndex: null));
    }

    [Fact]
    public void Route_index_pointing_at_an_apipa_only_adapter_falls_back_to_a_real_link()
    {
        var lan = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: true, 11, "192.168.1.50");
        var vpn = Ax("Corp VPN", NetworkInterfaceType.Ppp, hasGateway: false, 22, "169.254.5.5");

        var result = NetworkAdapterSelector.SelectBest(new[] { lan, vpn }, routeInterfaceIndex: 22);

        Assert.Equal("192.168.1.50", result!.IPv4);
    }

    [Theory]
    [InlineData(NetworkInterfaceType.Ppp)]
    [InlineData(NetworkInterfaceType.Tunnel)]
    public void Vpn_media_types_classify_as_vpn(NetworkInterfaceType type)
    {
        var a = Ax("Tunnel", type, hasGateway: false, 5, "10.0.0.9");
        Assert.True(NetworkAdapterSelector.IsVpn(a));
        Assert.Equal(NetworkAdapterSelector.ConnectionKind.Vpn, NetworkAdapterSelector.Classify(a));
    }

    [Fact]
    public void Vpn_client_name_classifies_as_vpn_even_on_ethernet_medium()
    {
        var a = Ax("Ethernet 3", NetworkInterfaceType.Ethernet, hasGateway: false, 5, "10.0.0.9",
            "Cisco AnyConnect Secure Mobility Client Virtual Adapter");
        Assert.True(NetworkAdapterSelector.IsVpn(a));
        Assert.Equal(NetworkAdapterSelector.ConnectionKind.Vpn, NetworkAdapterSelector.Classify(a));
    }

    [Fact]
    public void Classify_distinguishes_wifi_and_ethernet()
    {
        var wifi = Ax("Wi-Fi", NetworkInterfaceType.Wireless80211, hasGateway: true, 3, "172.16.4.20");
        var eth  = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: true, 4, "10.0.0.7");

        Assert.Equal(NetworkAdapterSelector.ConnectionKind.WiFi, NetworkAdapterSelector.Classify(wifi));
        Assert.Equal(NetworkAdapterSelector.ConnectionKind.Ethernet, NetworkAdapterSelector.Classify(eth));
    }

    [Fact]
    public void DescribeActiveAdapters_lists_all_with_the_routing_active_one_first()
    {
        var lan = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: true, 11, "192.168.170.131");
        var vpn = Ax("Corp VPN", NetworkInterfaceType.Ppp, hasGateway: false, 22, "195.169.220.167");

        var list = NetworkAdapterSelector.DescribeActiveAdapters(new[] { lan, vpn }, routeInterfaceIndex: 11);

        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsActive);
        Assert.Equal(NetworkAdapterSelector.ConnectionKind.Ethernet, list[0].Kind);
        Assert.Equal("192.168.170.131", list[0].IPv4);
        Assert.False(list[1].IsActive);
        Assert.Equal(NetworkAdapterSelector.ConnectionKind.Vpn, list[1].Kind);
        Assert.Equal("195.169.220.167", list[1].IPv4);
    }

    [Fact]
    public void DescribeActiveAdapters_puts_the_vpn_first_when_the_route_uses_it()
    {
        var lan = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: true, 11, "192.168.170.131");
        var vpn = Ax("Corp VPN", NetworkInterfaceType.Ppp, hasGateway: false, 22, "195.169.220.167");

        var list = NetworkAdapterSelector.DescribeActiveAdapters(new[] { lan, vpn }, routeInterfaceIndex: 22);

        Assert.True(list[0].IsActive);
        Assert.Equal(NetworkAdapterSelector.ConnectionKind.Vpn, list[0].Kind);
        Assert.Equal("195.169.220.167", list[0].IPv4);
    }

    [Fact]
    public void DescribeActiveAdapters_excludes_apipa_and_down_adapters()
    {
        var lan   = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: true, 11, "192.168.1.50");
        var apipa = Ax("Ethernet 2", NetworkInterfaceType.Ethernet, hasGateway: false, 12, "169.254.9.9");
        var down  = new NetworkAdapterSelector.AdapterInfo("Ethernet 3", "Down NIC",
            NetworkInterfaceType.Ethernet, IsUp: false, HasGateway: true,
            IPv4Addresses: new[] { "10.0.0.9" }, Id: "", InterfaceIndex: 13);

        var list = NetworkAdapterSelector.DescribeActiveAdapters(
            new[] { lan, apipa, down }, routeInterfaceIndex: 11);

        Assert.Single(list);
        Assert.Equal("192.168.1.50", list[0].IPv4);
        Assert.True(list[0].IsActive);
    }

    [Fact]
    public void DescribeActiveAdapters_returns_empty_when_no_usable_ipv4()
    {
        var apipa = Ax("Ethernet", NetworkInterfaceType.Ethernet, hasGateway: false, 11, "169.254.1.1");

        var list = NetworkAdapterSelector.DescribeActiveAdapters(new[] { apipa }, routeInterfaceIndex: 11);

        Assert.Empty(list);
    }
}
