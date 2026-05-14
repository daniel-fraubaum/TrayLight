using TrayLight.Models;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Providers;

public class OsVersionProviderTests
{
    [Fact]
    public async Task Reads_version_from_registry()
    {
        var sut = new OsVersionProvider(updatesAvailable: () => false);
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.False(string.IsNullOrWhiteSpace(data.Value));
        Assert.Contains("Windows", data.Value);
        Assert.False(data.HasWarning);
    }

    [Fact]
    public async Task Never_warns_even_when_updates_pending()
    {
        var sut = new OsVersionProvider(updatesAvailable: () => true);
        sut.Configure(new InfoItemConfig { ShowNotificationBadge = true });

        var data = await sut.GetDataAsync();

        Assert.False(data.HasWarning);
    }

    [Fact]
    public async Task No_warning_when_badge_disabled()
    {
        var sut = new OsVersionProvider(updatesAvailable: () => true);
        sut.Configure(new InfoItemConfig { ShowNotificationBadge = false });

        var data = await sut.GetDataAsync();

        Assert.False(data.HasWarning);
    }

    [Fact]
    public async Task Update_check_exception_is_swallowed()
    {
        var sut = new OsVersionProvider(updatesAvailable: () => throw new InvalidOperationException("boom"));
        sut.Configure(new InfoItemConfig { ShowNotificationBadge = true });

        var data = await sut.GetDataAsync();

        Assert.False(data.HasWarning);
        Assert.Contains("Windows", data.Value);
    }
}
