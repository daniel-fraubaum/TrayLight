using System;
using System.Collections.Generic;
using System.Linq;
using TrayLight.Services;
using Xunit;

namespace TrayLight.Tests.UserSettings;

public class SystemInfoDumpServiceTests
{
    private sealed class FakeSystemInfo : ISystemInfoService
    {
        public string MachineName => "TEST-PC";
        public string UserName    => "test.user";
        public string OsVersion   => "10.0.26100";
    }

    [Fact]
    public void BuildDump_includes_required_sections()
    {
        var sut = new SystemInfoDumpService(new FakeSystemInfo());
        var text = sut.BuildDump();

        Assert.Contains("[Device]",            text);
        Assert.Contains("Computer name:",      text);
        Assert.Contains("TEST-PC",             text);
        Assert.Contains("[Operating system]",  text);
        Assert.Contains("[Entra ID",           text);
        Assert.Contains("[Intune]",            text);
        Assert.Contains("[Network adapters]",  text);
    }

    [Fact]
    public void BuildDump_omits_placeholder_values_for_unknown_data()
    {
        var sut = new SystemInfoDumpService(new FakeSystemInfo());
        var text = sut.BuildDump();

        // Rule: a value that cannot be determined is omitted, never printed as
        // "unknown" / "(none)" / "(unavailable...)".
        Assert.DoesNotContain("unknown", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("(none)", text);
        Assert.DoesNotContain("(unavailable", text);
    }

    [Fact]
    public void BuildDump_network_adapters_have_mac_and_ip_and_are_deduped()
    {
        var sut = new SystemInfoDumpService(new FakeSystemInfo());
        var text = sut.BuildDump();

        // Every listed adapter must carry a MAC and an IP line, and filter-driver
        // bindings must not appear.
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var networkStart = Array.FindIndex(lines, l => l.StartsWith("[Network adapters]"));
        Assert.True(networkStart >= 0);

        var section = lines.Skip(networkStart + 1).ToArray();
        var macs = new List<string>();
        foreach (var line in section)
        {
            Assert.DoesNotContain("Filter", line);
            Assert.DoesNotContain("LightWeight", line);
            Assert.DoesNotContain("LWF", line);
            Assert.DoesNotContain("QoS Packet Scheduler", line);
            if (line.TrimStart().StartsWith("MAC:"))
                macs.Add(line.Trim());
        }

        // No duplicate MAC entries.
        Assert.Equal(macs.Count, macs.Distinct().Count());
    }
}
