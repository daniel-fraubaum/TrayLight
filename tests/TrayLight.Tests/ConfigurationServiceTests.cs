using TrayLight.Models;
using TrayLight.Services;
using TrayLight.Services.Configuration;
using Xunit;

namespace TrayLight.Tests;

/// <summary>
/// Verifies <see cref="ConfigurationService"/> reads from the abstracted
/// registry source and falls back to defaults for missing values.
/// </summary>
public class ConfigurationServiceTests
{
    [Fact]
    public void Load_EmptySource_ReturnsDefaults()
    {
        using var svc = new ConfigurationService(new InMemoryRegistrySource());

        var cfg = svc.Load();

        Assert.Equal("IT Support", cfg.Branding.Title);
        Assert.Equal("#0078D4",    cfg.Branding.AccentColor);
        Assert.True(cfg.Behavior.AutoStart);
        Assert.Equal(6, cfg.InfoItems.Count);
        Assert.Contains(cfg.InfoItems, i => i.Type == InfoItemType.ComputerName  && i.Enabled);
        Assert.Contains(cfg.InfoItems, i => i.Type == InfoItemType.OsVersion     && i.Enabled);
        Assert.Contains(cfg.InfoItems, i => i.Type == InfoItemType.LastReboot    && i.Enabled);
        Assert.Contains(cfg.InfoItems, i => i.Type == InfoItemType.StorageUsed   && i.Enabled);
        Assert.Contains(cfg.InfoItems, i => i.Type == InfoItemType.SerialNumber  && i.Enabled);
        Assert.Contains(cfg.InfoItems, i => i.Type == InfoItemType.IntuneSync    && i.Enabled);
        Assert.Empty(cfg.Shortcuts);
        Assert.NotNull(svc.LastLoadedUtc);
    }

    [Fact]
    public void Load_PopulatedBranding_OverridesDefaults()
    {
        var src = new InMemoryRegistrySource()
            .Set("Branding", "Title",       "Contoso IT")
            .Set("Branding", "AccentColor", "#112233")
            .Set("Branding", "CompanyName", "Contoso Ltd.");
        using var svc = new ConfigurationService(src);

        svc.Load();

        Assert.Equal("Contoso IT",   svc.Current.Branding.Title);
        Assert.Equal("#112233",      svc.Current.Branding.AccentColor);
        Assert.Equal("Contoso Ltd.", svc.Current.Branding.CompanyName);
    }

    [Fact]
    public void Load_HideAttribution_NotSet_DefaultsToVisible()
    {
        using var svc = new ConfigurationService(new InMemoryRegistrySource());

        var cfg = svc.Load();

        // Not configured => attribution line is visible (HideAttribution false).
        Assert.False(cfg.Branding.HideAttribution);
    }

    [Fact]
    public void Load_HideAttribution_Zero_KeepsAttributionVisible()
    {
        var src = new InMemoryRegistrySource()
            .Set("Branding", "HideAttribution", 0);
        using var svc = new ConfigurationService(src);

        svc.Load();

        Assert.False(svc.Current.Branding.HideAttribution);
    }

    [Fact]
    public void Load_HideAttribution_One_HidesAttribution()
    {
        var src = new InMemoryRegistrySource()
            .Set("Branding", "HideAttribution", 1);
        using var svc = new ConfigurationService(src);

        svc.Load();

        Assert.True(svc.Current.Branding.HideAttribution);
    }

    [Fact]
    public void Load_BehaviorAndLogging_AreReadAsDwords()
    {
        var src = new InMemoryRegistrySource()
            .Set("Behavior", "RefreshIntervalMinutes", 5)
            .Set("Behavior", "AutoStart", 1)
            .Set("Behavior", "NotifyOnUpdates", 0)
            .Set("Logging",  "LogRetentionDays", 14)
            .Set("Logging",  "MinimumLevel", "Warning");
        using var svc = new ConfigurationService(src);

        svc.Load();

        Assert.Equal(5, svc.Current.Behavior.RefreshIntervalMinutes);
        Assert.True(svc.Current.Behavior.AutoStart);
        Assert.False(svc.Current.Behavior.NotifyOnUpdates);
        Assert.Equal(14, svc.Current.Logging.LogRetentionDays);
        Assert.Equal("Warning", svc.Current.Logging.MinimumLevel);
    }

    [Fact]
    public void Load_InfoItemsKeyedByTypeName()
    {
        var src = new InMemoryRegistrySource()
            .Set("InfoItems\\ComputerName", "Position", 0)
            .Set("InfoItems\\StorageUsed",  "Position", 1)
            .Set("InfoItems\\StorageUsed",  "StorageLimit", 90);
        using var svc = new ConfigurationService(src);

        svc.Load();

        Assert.Equal(2, svc.Current.InfoItems.Count);
        Assert.Contains(svc.Current.InfoItems,
            i => i.Type == InfoItemType.ComputerName && i.Position == 0 && i.Enabled);
        Assert.Contains(svc.Current.InfoItems,
            i => i.Type == InfoItemType.StorageUsed && i.StorageLimit == 90);
    }

    [Fact]
    public void Load_ShortcutsAreOrderedByNumericKey()
    {
        var src = new InMemoryRegistrySource();
        // Add in reverse order to verify the reader sorts numerically.
        src.Set("Shortcuts\\10", "Title", "Tenth");
        src.Set("Shortcuts\\10", "ActionType", "url");
        src.Set("Shortcuts\\10", "Action", "https://10");
        src.Set("Shortcuts\\2",  "Title", "Second");
        src.Set("Shortcuts\\2",  "ActionType", "url");
        src.Set("Shortcuts\\2",  "Action", "https://2");
        src.Set("Shortcuts\\0",  "Title", "Zero");
        src.Set("Shortcuts\\0",  "ActionType", "url");
        src.Set("Shortcuts\\0",  "Action", "https://0");
        using var svc = new ConfigurationService(src);

        svc.Load();

        Assert.Equal(3, svc.Current.Shortcuts.Count);
        Assert.Equal("Zero",   svc.Current.Shortcuts[0].Title);
        Assert.Equal("Second", svc.Current.Shortcuts[1].Title);
        Assert.Equal("Tenth",  svc.Current.Shortcuts[2].Title);
        Assert.Equal(ShortcutActionType.Url, svc.Current.Shortcuts[0].ActionType);
    }

    [Fact]
    public void Load_RaisesPropertyChangedForCurrentAndLastLoaded()
    {
        var src = new InMemoryRegistrySource()
            .Set("Branding", "Title", "X");
        using var svc = new ConfigurationService(src);

        var raised = new List<string>();
        svc.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        svc.Load();

        Assert.Contains(nameof(IConfigurationService.Current),       raised);
        Assert.Contains(nameof(IConfigurationService.LastLoadedUtc), raised);
    }

    [Fact]
    public void ConfigPath_DescribesRegistryRoot()
    {
        using var svc = new ConfigurationService(new InMemoryRegistrySource());
        Assert.Equal("in-memory://TrayLight", svc.ConfigPath);
    }
}
