using TrayLight.Models;
using TrayLight.Services;
using TrayLight.Services.Providers;
using Xunit;

namespace TrayLight.Tests.Providers;

public class LastRebootProviderTests
{
    [Fact]
    public async Task Less_than_24h_uses_shared_uptime_formatter()
    {
        var uptime = TimeSpan.FromHours(3);
        var sut = new LastRebootProvider(
            uptimeProvider: () => uptime,
            shellRunner: (_, _) => { });
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        // Same shared, localized formatter the tile uses - never the old "Today".
        Assert.Equal(RelativeTimeFormatter.FormatUptime(uptime), data.Value);
        Assert.NotEqual("Today", data.Value);
        Assert.False(data.HasWarning);
    }

    [Fact]
    public async Task Multi_day_uptime_uses_shared_uptime_formatter()
    {
        var uptime = TimeSpan.FromHours(24 * 5 + 7);
        var sut = new LastRebootProvider(
            uptimeProvider: () => uptime,
            shellRunner: (_, _) => { });
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.Equal(RelativeTimeFormatter.FormatUptime(uptime), data.Value);
    }

    [Fact]
    public async Task Warns_when_uptime_exceeds_global_reboot_warning_days()
    {
        var sut = new LastRebootProvider(
            uptimeProvider: () => TimeSpan.FromDays(15),
            shellRunner: (_, _) => { },
            warningDaysProvider: () => 7);
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.True(data.HasWarning);
        Assert.Contains("15", data.WarningMessage);
    }

    [Fact]
    public async Task Does_not_warn_when_warning_days_is_zero()
    {
        var sut = new LastRebootProvider(
            uptimeProvider: () => TimeSpan.FromDays(365),
            shellRunner: (_, _) => { },
            warningDaysProvider: () => 0);
        sut.Configure(new InfoItemConfig());

        var data = await sut.GetDataAsync();

        Assert.False(data.HasWarning);
    }

    [Fact]
    public async Task Click_invokes_shutdown_with_restart_args()
    {
        string? file = null, args = null;
        var sut = new LastRebootProvider(
            uptimeProvider: () => TimeSpan.FromHours(1),
            shellRunner: (f, a) => { file = f; args = a; });
        sut.Configure(new InfoItemConfig());

        await sut.ExecuteClickAsync();

        Assert.Equal("shutdown.exe", file);
        Assert.Contains("/r", args);
        Assert.Contains("/t 60", args);
        Assert.Contains("/f", args);
    }
}
