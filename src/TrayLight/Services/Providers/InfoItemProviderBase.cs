using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TrayLight.Models;
using TrayLight.Services.Logging;

namespace TrayLight.Services.Providers;

/// <summary>
/// Shared plumbing for <see cref="IInfoItemProvider"/> implementations:
/// thread-safe caching of the last value, periodic refresh on a
/// <see cref="System.Threading.Timer"/>, exception swallowing with a graceful
/// "Not available" fallback, and Windows Event-Log logging of failures.
/// </summary>
public abstract class InfoItemProviderBase : IInfoItemProvider, IDisposable
{
    private const string EventLogSource = "TrayLight";
    private const string EventLogName = "Application";
    private const int FailureEventId = 1100;

    /// <summary>
    /// Optional structured logger. Set by DI when the provider is resolved
    /// from the container; remains <see cref="NullLogger"/> in tests that
    /// instantiate providers directly.
    /// </summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private InfoItemData? _last;
    private InfoItemConfig? _config;
    private bool _started;
    private bool _disposed;

    public abstract string Type { get; }

    /// <summary>Default title shown when the user did not override it.</summary>
    protected abstract string DefaultTitle { get; }

    /// <summary>Default Fluent icon glyph for the tile.</summary>
    protected abstract string DefaultIcon { get; }

    /// <summary>Override-friendly accessor exposing the live config (may be null).</summary>
    protected InfoItemConfig? Config { get { lock (_gate) return _config; } }

    public event EventHandler<InfoItemData>? DataChanged;

    public void Configure(InfoItemConfig config)
    {
        lock (_gate) _config = config;
    }

    public void Start(TimeSpan refreshInterval)
    {
        lock (_gate)
        {
            if (_disposed || _started) return;
            _started = true;
            // Fire one immediate refresh and then every <interval>. A negative
            // dueTime of 0 yields the first run on the threadpool right away.
            _timer = new System.Threading.Timer(
                _ => _ = RefreshAsync(),
                state: null,
                dueTime: TimeSpan.Zero,
                period: refreshInterval <= TimeSpan.Zero ? Timeout.InfiniteTimeSpan : refreshInterval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _started = false;
        }
    }

    public async Task<InfoItemData> GetDataAsync(CancellationToken cancellationToken = default)
    {
        InfoItemData? snapshot;
        lock (_gate) snapshot = _last;
        if (snapshot is not null) return snapshot;
        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteClickAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await OnClickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError($"Click handler for '{Type}' failed", ex);
        }
    }

    /// <summary>Refreshes the value now, returning the new snapshot.</summary>
    public async Task<InfoItemData> RefreshAsync(CancellationToken cancellationToken = default)
    {
        InfoItemData snapshot;
        try
        {
            snapshot = await CollectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"Provider '{Type}' refresh failed", ex);
            snapshot = InfoItemData.Unavailable(EffectiveTitle, EffectiveIcon, ex.Message);
        }

        bool changed;
        lock (_gate)
        {
            changed = _last != snapshot;
            _last = snapshot;
        }
        if (changed)
        {
            DataChanged?.Invoke(this, snapshot);
            if (snapshot.HasWarning)
                Logger.LogWarning(LogEvents.InfoItemWarning,
                    "Info item '{Type}' raised a warning: {Message}",
                    Type, snapshot.WarningMessage);
            else
                Logger.LogInformation(LogEvents.InfoItemUpdated,
                    "Info item '{Type}' updated: {Value}",
                    Type, snapshot.Value);
        }
        return snapshot;
    }

    /// <summary>The actual data-collection logic (called on the threadpool).</summary>
    protected abstract Task<InfoItemData> CollectAsync(CancellationToken cancellationToken);

    /// <summary>Optional click handler. Default: no-op.</summary>
    protected virtual Task OnClickAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Title respecting any user override.</summary>
    protected string EffectiveTitle =>
        Config?.Title is { Length: > 0 } t ? t : DefaultTitle;

    /// <summary>Icon respecting any user override.</summary>
    protected string EffectiveIcon =>
        Config?.Icon is { Length: > 0 } i ? i : DefaultIcon;

    /// <summary>
    /// Best-effort launch of a URI / executable. Used by all providers whose
    /// click handlers should open Windows Settings or Company Portal.
    /// </summary>
    [SupportedOSPlatform("windows")]
    protected static void LaunchShell(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    /// <summary>Writes an error to the Windows Event Log (best effort).</summary>
    protected void LogError(string message, Exception? ex = null)
    {
        // Prefer the structured pipeline when DI has wired one up; fall back
        // to a direct EventLog write so providers instantiated outside the
        // container (tests, design-time) still surface failures.
        if (Logger is not NullLogger)
        {
            Logger.LogWarning(LogEvents.InfoItemWarning, ex, "{Message}", message);
            return;
        }

        try
        {
            if (!OperatingSystem.IsWindows()) return;
            if (!EventLog.SourceExists(EventLogSource))
                EventLog.CreateEventSource(EventLogSource, EventLogName);
            var text = ex is null ? message : $"{message}: {ex}";
            EventLog.WriteEntry(EventLogSource, text, EventLogEntryType.Warning, FailureEventId);
        }
        catch
        {
            // Even logging may fail (no permission, source not registered).
            // We deliberately swallow to never crash the UI from a provider.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
        GC.SuppressFinalize(this);
    }
}
