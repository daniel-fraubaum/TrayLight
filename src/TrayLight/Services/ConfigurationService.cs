using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using TrayLight.Models;
using TrayLight.Services.Configuration;
using TrayLight.Services.Logging;

namespace TrayLight.Services;

/// <summary>
/// Reads <see cref="AppConfiguration"/> from the Windows registry under
/// <c>HKLM\SOFTWARE\Policies\TrayLight</c> (deployed via Intune Settings
/// Catalog or Group Policy ADMX). Polls periodically for changes so that
/// MDM-pushed updates take effect without restarting the app.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private const string EventLogSource = "TrayLight";
    private const string EventLogName   = "Application";

    /// <summary>How often the registry is re-read while watching.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly IRegistryConfigurationSource _source;
    private System.Threading.Timer? _pollTimer;
    private bool _disposed;
    private ILogger<ConfigurationService>? _logger;

    private AppConfiguration _current = AppConfiguration.CreateDefault();
    private DateTime? _lastLoadedUtc;
    // Hash of the last successfully-loaded config. Used so the periodic
    // 1-minute poll only stamps LastLoadedUtc when the policy actually
    // changed - otherwise the footer would always read "just now".
    private string? _lastConfigHash;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Human-readable description of the configuration source.</summary>
    public string ConfigPath => _source.RootDescription;

    public AppConfiguration Current
    {
        get { lock (_gate) return _current; }
        private set
        {
            lock (_gate) _current = value;
            OnPropertyChanged();
        }
    }

    public DateTime? LastLoadedUtc
    {
        get { lock (_gate) return _lastLoadedUtc; }
        private set
        {
            lock (_gate) _lastLoadedUtc = value;
            OnPropertyChanged();
        }
    }

    [SupportedOSPlatform("windows")]
    public ConfigurationService()
        : this(new HklmPolicyRegistrySource()) { }

    /// <summary>Test-friendly constructor that takes any registry source.</summary>
    public ConfigurationService(IRegistryConfigurationSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public void AttachLogger(ILogger<ConfigurationService> logger) => _logger = logger;

    public AppConfiguration Load()
    {
        try
        {
            var hasAnyPolicy = _source.GetSubKeyNames(string.Empty).Count > 0;

            var parsed = RegistryConfigurationReader.Read(_source);
            ApplyDefaults(parsed);

            var errors = Validate(parsed);
            if (errors.Count > 0)
            {
                Report(LogEvents.ConfigError, LogLevel.Error,
                    "Configuration validation failed:" + Environment.NewLine +
                    string.Join(Environment.NewLine, errors.ConvertAll(e => " - " + e)));
                return Current;
            }

            Current = parsed;
            var firstLoad = _lastLoadedUtc is null;

            // Only refresh the "last refreshed" stamp when the configuration
            // actually changed; otherwise an idle device would constantly
            // show "just now" because of the 60-second poll cadence.
            var hash = ComputeHash(parsed);
            if (firstLoad || !string.Equals(hash, _lastConfigHash, StringComparison.Ordinal))
            {
                _lastConfigHash = hash;
                LastLoadedUtc = DateTime.UtcNow;
            }

            if (!hasAnyPolicy && firstLoad)
            {
                Report(LogEvents.ConfigError, LogLevel.Warning,
                    $"No TrayLight policy found at {_source.RootDescription}. " +
                    "Built-in defaults are being used.");
            }

            Report(LogEvents.ConfigLoaded, LogLevel.Information,
                $"Configuration loaded from {_source.RootDescription}.");
        }
        catch (Exception ex)
        {
            Report(LogEvents.ConfigError, LogLevel.Error,
                $"Unexpected error reading {_source.RootDescription}: {ex}");
        }

        return Current;
    }

    public void StartWatching()
    {
        if (_pollTimer is not null) return;
        _pollTimer = new System.Threading.Timer(_ => Load(), null, PollInterval, PollInterval);
    }

    /// <summary>
    /// Validates a configuration. Returns a list of human-readable error
    /// messages; an empty list means the config is valid.
    /// </summary>
    public static List<string> Validate(AppConfiguration config)
    {
        var errors = new List<string>();
        if (config is null)
        {
            errors.Add("Configuration is null.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(config.Branding.Title))
            errors.Add("branding.title must not be empty.");
        if (!string.IsNullOrEmpty(config.Branding.AccentColor) && !IsHexColor(config.Branding.AccentColor))
            errors.Add($"branding.accentColor '{config.Branding.AccentColor}' is not a valid hex color (e.g. '#0078D4').");

        if (config.Behavior.RefreshIntervalMinutes < 0)
            errors.Add("behavior.refreshIntervalMinutes must be >= 0.");

        for (int i = 0; i < config.InfoItems.Count; i++)
        {
            var item = config.InfoItems[i];
            if (item.Type == InfoItemType.Unknown)
                errors.Add($"infoItems[{i}].type is missing or unknown.");
            if (item.Position < -1 || item.Position > 7)
                errors.Add($"infoItems[{i}].position must be -1 or in range 0..7 (was {item.Position}).");
            if (item.StorageLimit is < 0 or > 100)
                errors.Add($"infoItems[{i}].storageLimit must be in range 0..100 (was {item.StorageLimit}).");
            if (item.UptimeDaysLimit is < 0)
                errors.Add($"infoItems[{i}].uptimeDaysLimit must be >= 0 (was {item.UptimeDaysLimit}).");
        }

        for (int i = 0; i < config.Shortcuts.Count; i++)
        {
            var s = config.Shortcuts[i];
            if (string.IsNullOrWhiteSpace(s.Title))
                errors.Add($"shortcuts[{i}].title must not be empty.");
            if (s.ActionType == ShortcutActionType.Unknown)
                errors.Add($"shortcuts[{i}].actionType is missing or unknown (expected 'url', 'app' or 'command').");
            if (string.IsNullOrWhiteSpace(s.Action))
                errors.Add($"shortcuts[{i}].action must not be empty.");
            if (s.Position < -1)
                errors.Add($"shortcuts[{i}].position must be >= -1 (was {s.Position}).");
        }

        return errors;
    }

    private static void ApplyDefaults(AppConfiguration config)
    {
        config.Branding ??= new BrandingConfig();
        config.Footer   ??= new FooterConfig();
        config.Behavior ??= new BehaviorConfig();
        config.Logging  ??= new LoggingConfig();
        config.InfoItems ??= new List<InfoItemConfig>();
        config.Shortcuts ??= new List<ShortcutConfig>();

        if (string.IsNullOrWhiteSpace(config.Branding.Title))
            config.Branding.Title = "IT Support";
        if (string.IsNullOrWhiteSpace(config.Branding.AccentColor))
            config.Branding.AccentColor = "#0078D4";

        // If the registry contains no info-item subkeys at all, fall back to
        // the default tile set so the popup is never empty on a fresh device.
        if (config.InfoItems.Count == 0)
            config.InfoItems = AppConfiguration.CreateDefault().InfoItems;
    }

    private static bool IsHexColor(string value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '#') return false;
        if (value.Length is not (4 or 7 or 9)) return false;
        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            bool isHex = (c >= '0' && c <= '9')
                      || (c >= 'a' && c <= 'f')
                      || (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static void WriteEventLog(string message, EventLogEntryType type, int eventId)
    {
        try
        {
            if (!EventLog.SourceExists(EventLogSource))
            {
                try { EventLog.CreateEventSource(EventLogSource, EventLogName); }
                catch { /* not elevated -- write below will throw, caught */ }
            }
            EventLog.WriteEntry(EventLogSource, message, type, eventId);
        }
        catch
        {
            Debug.WriteLine($"[TrayLight][{type}] {message}");
        }
    }

    private void Report(Microsoft.Extensions.Logging.EventId eventId, LogLevel level, string message)
    {
        if (_logger is not null)
        {
            _logger.Log(level, eventId, message);
            return;
        }

        if (!OperatingSystem.IsWindows()) return;
        var type = level switch
        {
            LogLevel.Critical or LogLevel.Error => EventLogEntryType.Error,
            LogLevel.Warning                    => EventLogEntryType.Warning,
            _                                   => EventLogEntryType.Information,
        };
        WriteEventLog(message, type, eventId.Id);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string ComputeHash(AppConfiguration config)
    {
        // Stable JSON projection of the policy state so we can detect
        // whether anything actually changed since the last successful load.
        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
