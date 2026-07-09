using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TrayLight.Resources;
using TrayLight.Services;
using TrayLight.ViewModels;
using Xunit;

namespace TrayLight.Tests.Localization;

/// <summary>
/// Validates the runtime, file-based JSON localization pipeline: German and
/// French JSON files resolve correctly, regional cultures fall back to their base
/// language, unsupported cultures fall back to English, the fallback chain
/// (exact culture &gt; language &gt; English) is honoured, corrupt or missing
/// files degrade gracefully, and the view-model formatters honour the active UI
/// culture.
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

    // ---- File-based fallback chain ----------------------------------------

    private static string CreateTempLanguages(Dictionary<string, string> files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "TrayLightLang_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (tag, content) in files)
            File.WriteAllText(Path.Combine(dir, tag + ".json"), content);
        return dir;
    }

    [Fact]
    public void Fallback_chain_prefers_exact_culture_then_language_then_english()
    {
        var dir = CreateTempLanguages(new Dictionary<string, string>
        {
            ["en"]    = "{ \"Greeting\": \"Hello\", \"EnglishOnly\": \"OnlyInEnglish\" }",
            ["de"]    = "{ \"Greeting\": \"Hallo\" }",
            ["de-AT"] = "{ \"Greeting\": \"Servus\" }",
        });
        try
        {
            var svc = new LocalizationService(dir);

            // Exact culture file wins.
            Assert.Equal("Servus", svc.GetString("Greeting", CultureInfo.GetCultureInfo("de-AT")));
            // Language file used when no exact culture file exists.
            Assert.Equal("Hallo", svc.GetString("Greeting", CultureInfo.GetCultureInfo("de-DE")));
            // Key missing from the translation falls back to the English base.
            Assert.Equal("OnlyInEnglish", svc.GetString("EnglishOnly", CultureInfo.GetCultureInfo("de-AT")));
            // Unsupported culture falls back to English.
            Assert.Equal("Hello", svc.GetString("Greeting", CultureInfo.GetCultureInfo("es-ES")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_translation_file_falls_back_to_english_without_throwing()
    {
        var dir = CreateTempLanguages(new Dictionary<string, string>
        {
            ["en"] = "{ \"Greeting\": \"Hello\" }",
            ["de"] = "{ this is not valid json ",
        });
        try
        {
            var svc = new LocalizationService(dir);

            // The corrupt de.json overlay is skipped; the English base is used.
            Assert.Equal("Hello", svc.GetString("Greeting", CultureInfo.GetCultureInfo("de-DE")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_english_base_falls_back_to_embedded_defaults()
    {
        var dir = CreateTempLanguages(new Dictionary<string, string>
        {
            ["en"] = "{ broken json ",
        });
        try
        {
            var svc = new LocalizationService(dir);

            // en.json is unreadable, so the embedded English reference is used.
            Assert.Equal("Storage", svc.GetString("TileStorage", CultureInfo.GetCultureInfo("en-US")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_languages_folder_falls_back_to_embedded_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TrayLightLangMissing_" + Guid.NewGuid().ToString("N"));
        // Intentionally not created.

        var svc = new LocalizationService(dir);

        // No files at all -> embedded en.json supplies the reference values.
        Assert.Equal("Computer Name", svc.GetString("TileComputerName", CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void Unknown_key_returns_the_key_itself()
    {
        var dir = CreateTempLanguages(new Dictionary<string, string>
        {
            ["en"] = "{ \"Greeting\": \"Hello\" }",
        });
        try
        {
            var svc = new LocalizationService(dir);
            Assert.Equal("NoSuchKey", svc.GetString("NoSuchKey", CultureInfo.GetCultureInfo("en-US")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
