using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TrayLight.Services.Logging;

namespace TrayLight.Services;

/// <summary>
/// Resolves the configured branding logo (URL or local file path) to a
/// usable on-disk file path that WPF can bind to. URLs are downloaded
/// to %ProgramData%\TrayLight\Cache\logo.png and refreshed once per day.
/// Local file paths are watched with FileSystemWatcher so manual updates
/// take effect without a restart.
/// </summary>
public interface ILogoService : INotifyPropertyChanged
{
    /// <summary>Empty when no logo is configured / resolvable.</summary>
    string ResolvedLogoPath { get; }

    /// <summary>Force a re-resolve (re-download URL or re-read file).</summary>
    void Refresh();
}

public sealed partial class LogoService : ObservableObject, ILogoService, IDisposable
{
    private static readonly TimeSpan UrlRefreshInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan HttpTimeout        = TimeSpan.FromSeconds(10);

    private readonly IConfigurationService _config;
    private readonly ILogger<LogoService>  _log;
    private readonly HttpClient _http;

    private readonly object _lock = new();
    private string _currentSource = string.Empty;
    private FileSystemWatcher? _watcher;
    private Timer? _urlRefreshTimer;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty] private string _resolvedLogoPath = string.Empty;

    /// <summary>Cache directory for downloaded URL logos.</summary>
    public static string CacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     "TrayLight", "Cache");

    public static string CachedLogoPath => Path.Combine(CacheDirectory, "logo.png");

    /// <summary>Pack URI for the embedded default logo (used when ADMX
    /// doesn't supply a custom one).</summary>
    public const string DefaultLogoPackUri =
        "pack://application:,,,/TrayLight;component/Assets/default-logo.png";

    public LogoService(IConfigurationService config, ILogger<LogoService> log)
    {
        _config = config;
        _log    = log;
        _http   = new HttpClient { Timeout = HttpTimeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TrayLight/1.0");

        _config.PropertyChanged += OnConfigChanged;
        ApplyFromConfig(_config.Current.Branding.Logo);
    }

    public void Refresh() => ApplyFromConfig(_config.Current.Branding.Logo);

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IConfigurationService.Current)) return;
        var newSource = _config.Current.Branding.Logo ?? string.Empty;
        if (string.Equals(newSource, _currentSource, StringComparison.OrdinalIgnoreCase)) return;
        ApplyFromConfig(newSource);
    }

    private void ApplyFromConfig(string? source)
    {
        source ??= string.Empty;
        lock (_lock)
        {
            _currentSource = source;
            DisposeWatcher();
            DisposeUrlTimer();
            _downloadCts?.Cancel();
            _downloadCts = null;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            ResolvedLogoPath = DefaultLogoPackUri;
            return;
        }

        if (IsHttpUrl(source))
        {
            // If a previous download is cached, surface it immediately so the
            // UI doesn't flicker while we re-download in the background.
            if (File.Exists(CachedLogoPath))
                ResolvedLogoPath = CachedLogoPath;

            StartUrlRefreshTimer(source);
            _ = DownloadAsync(source);
        }
        else
        {
            // Local file path.
            ResolvedLogoPath = File.Exists(source) ? source : string.Empty;
            StartFileWatcher(source);
        }
    }

    private static bool IsHttpUrl(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private void StartUrlRefreshTimer(string source)
    {
        _urlRefreshTimer = new Timer(_ => _ = DownloadAsync(source),
            state: null,
            dueTime: UrlRefreshInterval,
            period: UrlRefreshInterval);
    }

    private async Task DownloadAsync(string url)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            _downloadCts?.Cancel();
            _downloadCts = cts = new CancellationTokenSource();
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);

            using var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // Write to a temp file then atomically replace, so a partial
            // download never overwrites the previous good logo.
            var tmp = CachedLogoPath + ".tmp";
            await using (var fs = File.Create(tmp))
                await response.Content.CopyToAsync(fs, cts.Token).ConfigureAwait(false);

            File.Move(tmp, CachedLogoPath, overwrite: true);
            ResolvedLogoPath = CachedLogoPath;

            _log.LogInformation(LogEvents.ConfigLoaded,
                "Logo downloaded from {Url} to {Cache}.", url, CachedLogoPath);
        }
        catch (OperationCanceledException) { /* expected on config change */ }
        catch (Exception ex)
        {
            // Fall back to cached version if available.
            if (File.Exists(CachedLogoPath))
            {
                ResolvedLogoPath = CachedLogoPath;
                _log.LogWarning(ex,
                    "Logo download from {Url} failed; using cached copy at {Cache}.",
                    url, CachedLogoPath);
            }
            else
            {
                ResolvedLogoPath = string.Empty;
                _log.LogWarning(ex, "Logo download from {Url} failed and no cache exists.", url);
            }
        }
    }

    private void StartFileWatcher(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        var name = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name) || !Directory.Exists(dir))
            return;

        try
        {
            var watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            watcher.Changed += (_, _) => OnFileChanged(filePath);
            watcher.Created += (_, _) => OnFileChanged(filePath);
            watcher.Renamed += (_, _) => OnFileChanged(filePath);
            watcher.Deleted += (_, _) => OnFileChanged(filePath);
            _watcher = watcher;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not watch logo file {Path}.", filePath);
        }
    }

    private void OnFileChanged(string filePath)
    {
        // Bump the property even if the path string is unchanged, so WPF
        // re-loads the BitmapImage from disk.
        ResolvedLogoPath = string.Empty;
        ResolvedLogoPath = File.Exists(filePath) ? filePath : string.Empty;
    }

    private void DisposeWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void DisposeUrlTimer()
    {
        _urlRefreshTimer?.Dispose();
        _urlRefreshTimer = null;
    }

    public void Dispose()
    {
        _config.PropertyChanged -= OnConfigChanged;
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        DisposeWatcher();
        DisposeUrlTimer();
        _http.Dispose();
    }
}
