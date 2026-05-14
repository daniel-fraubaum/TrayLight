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
}
