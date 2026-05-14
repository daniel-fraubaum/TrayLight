using TrayLight.Models;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Providers;

public class StorageUsedProviderTests
{
    [Fact]
    public async Task Formats_used_total_and_percent()
    {
        // 256 GB total, 64 GB free -> 192 GB used (75%)
        var sut = new StorageUsedProvider(() => new StorageUsedProvider.DriveStats(
            TotalBytes: 256L * 1024 * 1024 * 1024,
            FreeBytes:  64L  * 1024 * 1024 * 1024));
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal("192 / 256 GB (75%)", data.Value);
        Assert.Equal("64 GB free", data.DetailText);
        Assert.False(data.HasWarning);
    }

    [Fact]
    public async Task Warns_when_threshold_exceeded()
    {
        var sut = new StorageUsedProvider(() => new StorageUsedProvider.DriveStats(
            TotalBytes: 100L * 1024 * 1024 * 1024,
            FreeBytes:   5L  * 1024 * 1024 * 1024));
        sut.Configure(new InfoItemConfig { StorageLimit = 90 });

        var data = await sut.GetDataAsync();

        Assert.True(data.HasWarning);
        Assert.Contains("95%", data.WarningMessage);
    }

    [Fact]
    public async Task Returns_unavailable_when_drive_missing()
    {
        var sut = new StorageUsedProvider(() => null);
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal("Not available", data.Value);
    }
}
