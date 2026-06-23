using TrayLight.Models;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Providers;

public class IntuneSyncProviderTests
{
    [Fact]
    public async Task Not_enrolled_does_not_warn()
    {
        var sut = new IntuneSyncProvider(() => new IntuneSyncProvider.IntuneStatus(false, null));
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal("Not enrolled", data.Value);
        Assert.False(data.HasWarning);
    }

    [Fact]
    public async Task Enrolled_with_recent_sync_does_not_warn()
    {
        var sut = new IntuneSyncProvider(() => new IntuneSyncProvider.IntuneStatus(
            true, DateTime.UtcNow.AddHours(-2)));
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal("Enrolled", data.Value);
        Assert.False(data.HasWarning);
        Assert.Contains("Last sync", data.DetailText);
    }

    [Fact]
    public async Task Enrolled_with_stale_sync_does_not_warn()
    {
        var sut = new IntuneSyncProvider(() => new IntuneSyncProvider.IntuneStatus(
            true, DateTime.UtcNow.AddDays(-30)));
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal("Enrolled", data.Value);
        Assert.False(data.HasWarning);
    }
}
