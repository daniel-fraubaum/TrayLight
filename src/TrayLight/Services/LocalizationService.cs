using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace TrayLight.Services;

/// <summary>
/// Detects the active Windows display language on startup and confirms it is a
/// supported UI culture. TrayLight ships English (neutral fallback), German and
/// French; any other display language automatically falls back to English via
/// the <see cref="System.Resources.ResourceManager"/> parent-culture chain, so
/// this service only needs to make the detected culture explicit and ensure WPF
/// elements format dates/numbers for the same culture.
/// </summary>
public static class LocalizationService
{
    /// <summary>UI cultures that ship a dedicated satellite resource assembly.</summary>
    public static readonly IReadOnlyList<string> SupportedLanguages =
        new[] { "en", "de", "fr" };

    /// <summary>
    /// Resolves the closest supported language for the current
    /// <see cref="CultureInfo.CurrentUICulture"/> (e.g. <c>de-AT</c> matches
    /// <c>de</c>), falling back to English. The two-letter language tag is
    /// returned for logging/diagnostics; resource resolution itself is handled
    /// transparently by the <see cref="System.Resources.ResourceManager"/>.
    /// </summary>
    public static string ResolveLanguage(CultureInfo uiCulture)
    {
        var lang = uiCulture.TwoLetterISOLanguageName;
        return SupportedLanguages.Contains(lang, StringComparer.OrdinalIgnoreCase)
            ? lang
            : "en";
    }

    /// <summary>
    /// Applies the detected UI culture process-wide and aligns WPF's default
    /// language so XAML-bound text formats consistently. Safe to call once at
    /// startup before any window is shown. Returns the resolved language tag.
    /// </summary>
    public static string Initialize()
    {
        var uiCulture = CultureInfo.CurrentUICulture;
        var lang = ResolveLanguage(uiCulture);

        // Make the detected UI culture the default for every thread so that
        // resource lookups (and any background-thread provider that formats
        // text) resolve to the same language as the UI thread.
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;

        // Align WPF's element language with the OS UI culture so date/number
        // runs render for the detected language instead of the en-US default.
        try
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    XmlLanguage.GetLanguage(uiCulture.IetfLanguageTag)));
        }
        catch
        {
            // OverrideMetadata throws if called twice (e.g. in tests). The
            // language alignment is best-effort and never fatal to startup.
        }

        return lang;
    }
}
