using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.Logging;

namespace TrayLight.Services;

/// <summary>
/// Runtime, file-based localization. Instead of compiled <c>.resx</c> satellite
/// assemblies, translations live in plain JSON files under a <c>Languages</c>
/// folder next to the executable (e.g. <c>C:\Program Files\TrayLight\Languages\</c>).
///
/// <para>
/// Resolution for the active <see cref="CultureInfo.CurrentUICulture"/>:
/// <list type="number">
///   <item><c>en.json</c> is ALWAYS loaded as the base, so any key missing from a
///   translation automatically falls back to English instead of showing blank.</item>
///   <item>An exact culture file is overlaid if present (e.g. <c>de-AT.json</c>),</item>
///   <item>otherwise the two-letter language file (e.g. <c>de.json</c>),</item>
///   <item>otherwise nothing is overlaid and the English base is used.</item>
/// </list>
/// </para>
///
/// <para>
/// Adding a language needs no rebuild: drop e.g. <c>nl.json</c> into the
/// <c>Languages</c> folder and restart TrayLight. When the folder is missing or a
/// file is corrupt, the service falls back to an embedded copy of <c>en.json</c>,
/// logs a warning and never throws.
/// </para>
/// </summary>
public sealed class LocalizationService
{
    /// <summary>Folder name (next to the exe) that holds the JSON language files.</summary>
    public const string LanguagesFolderName = "Languages";

    /// <summary>Manifest name of the embedded <c>en.json</c> last-resort fallback.</summary>
    private const string EmbeddedEnglishResourceName = "TrayLight.en.json";

    /// <summary>Languages that ship with the installer.</summary>
    public static readonly IReadOnlyList<string> SupportedLanguages =
        new[] { "en", "de", "fr" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<LocalizationService> LazyInstance = new(() => new LocalizationService());

    /// <summary>Process-wide default instance backing the <c>Strings</c> facade.</summary>
    public static LocalizationService Instance => LazyInstance.Value;

    /// <summary>
    /// Optional logger used by the default <see cref="Instance"/> for warnings.
    /// Set after the DI logging pipeline is up; safe to leave null.
    /// </summary>
    public static ILogger? SharedLogger { get; set; }

    private readonly string _languagesDir;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _tables =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _baseLock = new();
    private IReadOnlyDictionary<string, string>? _baseEnglish;

    /// <summary>
    /// Creates a service. <paramref name="languagesDir"/> defaults to the
    /// <c>Languages</c> folder next to the executable; tests inject a temp dir.
    /// </summary>
    public LocalizationService(string? languagesDir = null, ILogger? logger = null)
    {
        _languagesDir = languagesDir ?? Path.Combine(AppContext.BaseDirectory, LanguagesFolderName);
        _logger = logger;
    }

    private ILogger? EffectiveLogger => _logger ?? SharedLogger;

    /// <summary>Indexer form of <see cref="GetString(string)"/>.</summary>
    public string this[string key] => GetString(key);

    /// <summary>
    /// Looks up a localized string for the current UI culture, falling back to
    /// the English base and finally to the key itself (so typos are visible).
    /// </summary>
    public string GetString(string key) => GetString(key, CultureInfo.CurrentUICulture);

    /// <summary>Culture-explicit lookup, primarily for tests.</summary>
    internal string GetString(string key, CultureInfo uiCulture)
    {
        if (string.IsNullOrEmpty(key)) return key;
        var table = _tables.GetOrAdd(uiCulture.Name, _ => BuildTable(uiCulture));
        return table.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>
    /// Looks up a composite-format string and formats it with <paramref name="args"/>.
    /// </summary>
    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, GetString(key), args);

    private IReadOnlyDictionary<string, string> BuildTable(CultureInfo uiCulture)
    {
        var merged = new Dictionary<string, string>(GetBaseEnglish(), StringComparer.OrdinalIgnoreCase);

        // Overlay the best-matching translation: exact culture first (de-AT.json),
        // then the two-letter language (de.json). English needs no overlay - it
        // is already the base.
        if (!string.Equals(uiCulture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase))
        {
            var overlay = LoadFromFile(uiCulture.Name)
                       ?? LoadFromFile(uiCulture.TwoLetterISOLanguageName);
            if (overlay is not null)
                foreach (var kv in overlay)
                    merged[kv.Key] = kv.Value;
        }

        return merged;
    }

    private IReadOnlyDictionary<string, string> GetBaseEnglish()
    {
        if (_baseEnglish is not null) return _baseEnglish;
        lock (_baseLock)
        {
            if (_baseEnglish is not null) return _baseEnglish;

            var embedded = LoadEmbeddedEnglish();
            var fileEn = LoadFromFile("en");
            if (fileEn is null)
            {
                EffectiveLogger?.LogWarning(
                    "TrayLight localization: '{Dir}\\en.json' missing or invalid; using embedded English defaults.",
                    _languagesDir);
                _baseEnglish = embedded;
            }
            else
            {
                // File en.json wins over embedded so a corrected reference file
                // takes effect without a rebuild, but embedded fills any gaps.
                var merged = new Dictionary<string, string>(embedded, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in fileEn)
                    merged[kv.Key] = kv.Value;
                _baseEnglish = merged;
            }
            return _baseEnglish;
        }
    }

    private Dictionary<string, string>? LoadFromFile(string tag)
    {
        try
        {
            var path = Path.Combine(_languagesDir, tag + ".json");
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return dict is null ? null : new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            EffectiveLogger?.LogWarning(ex,
                "TrayLight localization: failed to read '{Tag}.json' in '{Dir}'; ignoring.", tag, _languagesDir);
            return null;
        }
    }

    private static Dictionary<string, string> LoadEmbeddedEnglish()
    {
        try
        {
            var asm = typeof(LocalizationService).Assembly;
            using var stream = asm.GetManifestResourceStream(EmbeddedEnglishResourceName);
            if (stream is null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return dict is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Absolute last resort: never throw from localization.
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Resolves the nearest built-in (shipped) language for the given UI culture
    /// (e.g. <c>de-AT</c> → <c>de</c>), falling back to English. Used for
    /// diagnostics/logging; actual string resolution goes through the JSON files.
    /// </summary>
    public static string ResolveLanguage(CultureInfo uiCulture)
    {
        var lang = uiCulture.TwoLetterISOLanguageName;
        return SupportedLanguages.Contains(lang, StringComparer.OrdinalIgnoreCase)
            ? lang
            : "en";
    }

    /// <summary>
    /// Aligns the process UI culture and WPF element language on startup, and
    /// primes the English base so any file/format problems are logged early.
    /// Safe to call once before any window is shown. Returns the resolved
    /// built-in language tag.
    /// </summary>
    public static string Initialize()
    {
        var uiCulture = CultureInfo.CurrentUICulture;
        var lang = ResolveLanguage(uiCulture);

        // Make the detected UI culture the default for every thread so that
        // background providers format text for the same language as the UI.
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;

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

        // Warm the cache for the active culture (also surfaces load warnings).
        _ = Instance.GetString(string.Empty, uiCulture);

        return lang;
    }
}
