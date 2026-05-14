using TrayLight.Models;
using TrayLight.Services;
using Xunit;

namespace TrayLight.Tests;

public class ConfigurationValidationTests
{
    [Fact]
    public void Validate_DefaultConfig_HasNoErrors()
    {
        var errors = ConfigurationService.Validate(AppConfiguration.CreateDefault());
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_EmptyTitle_ReportsError()
    {
        var config = AppConfiguration.CreateDefault();
        config.Branding.Title = "";

        var errors = ConfigurationService.Validate(config);

        Assert.Contains(errors, e => e.Contains("branding.title", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_BadAccentColor_ReportsError()
    {
        var config = AppConfiguration.CreateDefault();
        config.Branding.AccentColor = "not-a-color";

        var errors = ConfigurationService.Validate(config);

        Assert.Contains(errors, e => e.Contains("accentColor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#0078D4")]
    [InlineData("#80FF8800")]
    public void Validate_AcceptsValidHexColors(string color)
    {
        var config = AppConfiguration.CreateDefault();
        config.Branding.AccentColor = color;

        var errors = ConfigurationService.Validate(config);

        Assert.DoesNotContain(errors, e => e.Contains("accentColor", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_InfoItemPositionOutOfRange_ReportsError()
    {
        var config = AppConfiguration.CreateDefault();
        config.InfoItems.Add(new InfoItemConfig { Type = InfoItemType.ComputerName, Position = 6 });

        var errors = ConfigurationService.Validate(config);

        Assert.Contains(errors, e => e.Contains("position", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_StorageLimitOutOfRange_ReportsError()
    {
        var config = AppConfiguration.CreateDefault();
        config.InfoItems.Add(new InfoItemConfig
        {
            Type = InfoItemType.StorageUsed,
            Position = 0,
            Enabled = true,
            StorageLimit = 150
        });

        var errors = ConfigurationService.Validate(config);

        Assert.Contains(errors, e => e.Contains("storageLimit", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShortcutMissingActionType_ReportsError()
    {
        var config = AppConfiguration.CreateDefault();
        config.Shortcuts.Add(new ShortcutConfig
        {
            Title = "Helpdesk",
            Action = "https://example.com"
            // ActionType left as Unknown
        });

        var errors = ConfigurationService.Validate(config);

        Assert.Contains(errors, e => e.Contains("actionType", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShortcutMissingTitle_ReportsError()
    {
        var config = AppConfiguration.CreateDefault();
        config.Shortcuts.Add(new ShortcutConfig
        {
            Title = "",
            ActionType = ShortcutActionType.Url,
            Action = "https://example.com"
        });

        var errors = ConfigurationService.Validate(config);

        Assert.Contains(errors, e => e.Contains("title", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NegativeRefreshInterval_ReportsError()
    {
        var config = AppConfiguration.CreateDefault();
        config.Behavior.RefreshIntervalMinutes = -1;

        var errors = ConfigurationService.Validate(config);

        Assert.Contains(errors, e => e.Contains("refreshIntervalMinutes", StringComparison.Ordinal));
    }
}
