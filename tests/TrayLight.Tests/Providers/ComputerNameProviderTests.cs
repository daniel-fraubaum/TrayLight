using TrayLight.Models;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Providers;

public class ComputerNameProviderTests
{
    [Fact]
    public async Task GetData_returns_machine_name()
    {
        var sut = new ComputerNameProvider();
        sut.Configure(new InfoItemConfig { Type = InfoItemType.ComputerName });

        var data = await sut.GetDataAsync();

        Assert.Equal(Environment.MachineName, data.Value);
        Assert.False(data.HasWarning);
        Assert.Contains(Environment.UserName, data.DetailText);
    }

    [Fact]
    public async Task Configured_title_overrides_default()
    {
        var sut = new ComputerNameProvider();
        sut.Configure(new InfoItemConfig { Title = "Asset" });

        var data = await sut.GetDataAsync();

        Assert.Equal("Asset", data.Title);
    }
}
