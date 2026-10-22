using TrayLight.Models;
using TrayLight.Services;
using TrayLight.Services.Badges;
using TrayLight.Services.Configuration;
using Xunit;

namespace TrayLight.Tests.Badges;

/// <summary>
/// Precedence of the global Behavior\NotifyOnUpdates switch over the per-tile
/// InfoItems\&lt;Tile&gt;\ShowNotificationBadge value (#27).
/// </summary>
public class BadgePolicyTests
{
    [Theory]
    // Master Disabled (0) wins over every per-tile value.
    [InlineData(false, null,  false)]
    [InlineData(false, true,  false)]
    [InlineData(false, false, false)]
    // Master Enabled or Not Configured (both read as true): per-tile decides,
    // a missing per-tile value means on.
    [InlineData(true,  null,  true)]
    [InlineData(true,  true,  true)]
    [InlineData(true,  false, false)]
    public void Master_switch_takes_precedence_over_per_tile_value(
        bool notifyOnUpdates, bool? showNotificationBadge, bool expected)
    {
        var behavior = new BehaviorConfig { NotifyOnUpdates = notifyOnUpdates };
        var tile = new InfoItemConfig { Type = InfoItemType.LastReboot, ShowNotificationBadge = showNotificationBadge };

        Assert.Equal(expected, BadgePolicy.IsEnabled(behavior, tile));
    }

    [Theory]
    [InlineData(true,  true)]
    [InlineData(false, false)]
    public void Tile_without_config_follows_the_master_switch(bool notifyOnUpdates, bool expected)
    {
        var behavior = new BehaviorConfig { NotifyOnUpdates = notifyOnUpdates };
        Assert.Equal(expected, BadgePolicy.IsEnabled(behavior, tile: null));
    }

    [Theory]
    [InlineData("lastReboot")]
    [InlineData("storageUsed")]
    public void Provider_type_key_resolves_the_matching_tile(string typeKey)
    {
        var config = new AppConfiguration
        {
            Behavior = new BehaviorConfig { NotifyOnUpdates = true },
            InfoItems =
            {
                new InfoItemConfig { Type = InfoItemType.LastReboot,  ShowNotificationBadge = false },
                new InfoItemConfig { Type = InfoItemType.StorageUsed, ShowNotificationBadge = false },
            },
        };

        Assert.False(BadgePolicy.IsEnabled(config, typeKey));
    }

    [Fact]
    public void No_config_loaded_behaves_like_not_configured()
    {
        Assert.True(BadgePolicy.IsEnabled(config: null, "lastReboot"));
    }

    [Theory]
    // The reported case: master Disabled, per-tile left at the ADML default 1.
    [InlineData(0,    1,    false)]
    [InlineData(0,    null, false)]
    [InlineData(1,    0,    false)]
    [InlineData(1,    1,    true)]
    [InlineData(null, 1,    true)]
    [InlineData(null, 0,    false)]
    [InlineData(null, null, true)]
    public void Registry_values_follow_the_precedence(int? notifyOnUpdates, int? tileBadge, bool expected)
    {
        var src = new InMemoryRegistrySource()
            .Set("Behavior", "NotifyOnUpdates", notifyOnUpdates)
            .Set(@"InfoItems\LastReboot", "Position", 2)
            .Set(@"InfoItems\LastReboot", "ShowNotificationBadge", tileBadge);
        using var svc = new ConfigurationService(src);

        svc.Load();

        Assert.Equal(expected, BadgePolicy.IsEnabled(svc.Current, "lastReboot"));
    }
}
