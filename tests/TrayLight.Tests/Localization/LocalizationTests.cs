using System.Globalization;
using TrayLight.Resources;
using TrayLight.Services;
using TrayLight.ViewModels;
using Xunit;

namespace TrayLight.Tests.Localization;

/// <summary>
/// Validates the multi-language resource pipeline: the satellite assemblies for
/// German and French resolve correctly, unsupported cultures fall back to the
/// neutral English resource, and the regional formatters in the view-model honour
/// the active UI culture.
/// </summary>
public class LocalizationTests
{
    /// <summary>
    /// Runs <paramref name="body"/> with a fixed <see cref="CultureInfo.CurrentUICulture"/>
    /// and restores the original afterwards so tests stay isolated regardless of
    /// the host machine's display language.
    /// </summary>
    private static void WithUiCulture(string culture, Action body)
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var ci = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = ci;
            CultureInfo.CurrentCulture = ci;
            body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void English_is_the_neutral_fallback()
    {
        WithUiCulture("en-US", () =>
        {
            Assert.Equal("Computer Name", Strings.TileComputerName);
            Assert.Equal("Quick Actions", Strings.QuickActions);
            Assert.Equal("Not enrolled", Strings.StatusNotEnrolled);
        });
    }

    [Fact]
    public void German_resources_are_used_for_de()
    {
        WithUiCulture("de-DE", () =>
        {
            Assert.Equal("Computername", Strings.TileComputerName);
            Assert.Equal("Schnellzugriff", Strings.QuickActions);
            Assert.Equal("Nicht registriert", Strings.StatusNotEnrolled);
            Assert.Equal("Beenden", Strings.MenuQuit);
        });
    }

    [Fact]
    public void French_resources_are_used_for_fr()
    {
        WithUiCulture("fr-FR", () =>
        {
            Assert.Equal("Nom de l'ordinateur", Strings.TileComputerName);
            Assert.Equal("Actions rapides", Strings.QuickActions);
            Assert.Equal("Non inscrit", Strings.StatusNotEnrolled);
            Assert.Equal("Quitter", Strings.MenuQuit);
        });
    }

    [Fact]
    public void Regional_culture_falls_back_to_base_language()
    {
        // de-AT has no dedicated resource — it must resolve to the de resources.
        WithUiCulture("de-AT", () =>
            Assert.Equal("Computername", Strings.TileComputerName));

        // fr-CA falls back to the fr resources.
        WithUiCulture("fr-CA", () =>
            Assert.Equal("Reseau", Strings.TileNetwork));
    }

    [Fact]
    public void Unsupported_culture_falls_back_to_english()
    {
        // Spanish ships no resources, so the neutral English values are used.
        WithUiCulture("es-ES", () =>
        {
            Assert.Equal("Computer Name", Strings.TileComputerName);
            Assert.Equal("Storage", Strings.TileStorage);
        });
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("fr-FR", "fr")]
    [InlineData("fr-CA", "fr")]
    [InlineData("en-US", "en")]
    [InlineData("es-ES", "en")]
    [InlineData("ja-JP", "en")]
    public void ResolveLanguage_maps_to_nearest_supported(string uiCulture, string expected) =>
        Assert.Equal(expected, LocalizationService.ResolveLanguage(CultureInfo.GetCultureInfo(uiCulture)));

    [Fact]
    public void FormatRelative_is_localized()
    {
        WithUiCulture("en-US", () =>
        {
            Assert.Equal("just now", TrayPopupViewModel.FormatRelative(TimeSpan.FromSeconds(10)));
            Assert.Equal("5 minutes ago", TrayPopupViewModel.FormatRelative(TimeSpan.FromMinutes(5)));
            Assert.Equal("1 minute ago", TrayPopupViewModel.FormatRelative(TimeSpan.FromMinutes(1)));
        });

        WithUiCulture("de-DE", () =>
        {
            Assert.Equal("gerade eben", TrayPopupViewModel.FormatRelative(TimeSpan.FromSeconds(10)));
            Assert.Equal("vor 5 Minuten", TrayPopupViewModel.FormatRelative(TimeSpan.FromMinutes(5)));
        });

        WithUiCulture("fr-FR", () =>
            Assert.Equal("il y a 5 minutes", TrayPopupViewModel.FormatRelative(TimeSpan.FromMinutes(5))));
    }

    [Fact]
    public void Format_helper_substitutes_arguments()
    {
        WithUiCulture("de-DE", () =>
            Assert.Equal("75% belegt", Strings.Format("StoragePercentUsedFormat", 75)));

        WithUiCulture("en-US", () =>
            Assert.Equal("75% used", Strings.Format("StoragePercentUsedFormat", 75)));
    }
}
