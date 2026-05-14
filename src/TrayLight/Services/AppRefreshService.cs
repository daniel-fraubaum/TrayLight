using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TrayLight.Services.Logging;
using TrayLight.Services.Providers;

namespace TrayLight.Services;

/// <summary>
/// Application-level periodic refresh of every <see cref="IInfoItemProvider"/>.
/// Runs on a single <see cref="System.Threading.Timer"/> stored as an instance
/// field (so the timer is rooted by the singleton service and can never be
/// garbage-collected). The cadence comes from
/// <c>Behavior.RefreshIntervalMinutes</c> and is rebuilt whenever the
/// configuration changes.
/// </summary>
/// <remarks>
/// Lives for the entire process lifetime — independent of the popup window's
/// open/close state. The popup subscribes to <see cref="RefreshCompleted"/>
/// to learn when it should re-pull cached values from providers and update
/// its "Last refreshed" footer.
///
/// Subscribes to <see cref="SystemEvents.PowerModeChanged"/> so a wake from
/// sleep immediately triggers a catch-up refresh instead of waiting up to
/// one full interval for the next tick.
/// </remarks>
public sealed class AppRefreshService : IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IReadOnlyList<IInfoItemProvider> _providers;
    private readonly ILogger<AppRefreshService>? _logger;
    private readonly object _gate = new();

    private System.Threading.Timer? _timer;
    private TimeSpan _currentInterval = TimeSpan.Zero;
    private bool _started;
    private bool _disposed;
    private DateTime _lastRefreshUtc;

    public AppRefreshService(
        IConfigurationService configService,
        IEnumerable<IInfoItemProvider> providers,
        ILogger<AppRefreshService>? logger = null)
    {
        _configService = configService;
        _providers = providers.ToArray();
        _logger = logger;
        _lastRefreshUtc = DateTime.UtcNow;
    }

    /// <summary>UTC timestamp of the most recently completed refresh cycle.</summary>
    public DateTime LastRefreshUtc
    {
        get { lock (_gate) return _lastRefreshUtc; }
    }

    /// <summary>
    /// Raised on the application thread pool after every successful refresh
    /// cycle (including catch-up cycles after wake-from-sleep). Subscribers
    /// must marshal to the UI thread themselves.
    /// </summary>
    public event EventHandler<DateTime>? RefreshCompleted;

    /// <summary>Begin periodic refresh. Idempotent.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        _configService.PropertyChanged += OnConfigChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        RebuildTimer();
        _logger?.LogInformation(LogEvents.InfoItemUpdated,
            "AppRefreshService started (interval={Interval}).", _currentInterval);
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IConfigurationService.Current))
            RebuildTimer();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _logger?.LogInformation(LogEvents.InfoItemUpdated,
                "AppRefreshService: resume from sleep detected, triggering catch-up refresh.");
            // Run on the timer thread to avoid blocking the system event.
            _ = Task.Run(() => TickAsync(catchUp: true));
        }
    }

    private void RebuildTimer()
    {
        var minutes = Math.Max(1, _configService.Current.Behavior.RefreshIntervalMinutes);
        var interval = TimeSpan.FromMinutes(minutes);

        lock (_gate)
        {
            if (_disposed) return;
            if (_timer is not null && interval == _currentInterval) return;

            _timer?.Dispose();
            _currentInterval = interval;
            // Fire one immediate tick so providers cache fresh values right
            // away, then every <interval> after that.
            _timer = new System.Threading.Timer(
                _ => _ = TickAsync(catchUp: false),
                state: null,
                dueTime: TimeSpan.Zero,
                period: interval);
        }
        _logger?.LogInformation(LogEvents.InfoItemUpdated,
            "AppRefreshService: timer (re)scheduled, interval={Interval}.", interval);
    }

    private async Task TickAsync(bool catchUp)
    {
        var startedAt = DateTime.UtcNow;
        _logger?.LogInformation(LogEvents.InfoItemUpdated,
            "AppRefreshService: refresh cycle started at {StartedAt:o} (catchUp={CatchUp}).",
            startedAt, catchUp);

        var refreshed = 0;
        foreach (var provider in _providers)
        {
            try
            {
                _ = await provider.GetDataAsync().ConfigureAwait(false);
                refreshed++;
            }
            catch (Exception ex)
            {
                // Per-provider failures must NOT kill the timer.
                _logger?.LogWarning(LogEvents.InfoItemWarning,
                    "AppRefreshService: provider '{Type}' refresh failed: {Error}",
                    provider.Type, ex.Message);
            }
        }

        DateTime completedAt;
        lock (_gate)
        {
            _lastRefreshUtc = DateTime.UtcNow;
            completedAt = _lastRefreshUtc;
        }

        _logger?.LogInformation(LogEvents.InfoItemUpdated,
            "AppRefreshService: refresh cycle completed at {CompletedAt:o}, {Count}/{Total} providers refreshed (took {Duration}).",
            completedAt, refreshed, _providers.Count, completedAt - startedAt);

        try { RefreshCompleted?.Invoke(this, completedAt); }
        catch (Exception ex)
        {
            _logger?.LogWarning(LogEvents.InfoItemWarning,
                "AppRefreshService: RefreshCompleted handler threw: {Error}", ex.Message);
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
        try { _configService.PropertyChanged -= OnConfigChanged; } catch { /* shutting down */ }
        try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; } catch { /* shutting down */ }
    }
}
