using TrayLight.Models;

namespace TrayLight.Services.Providers;

/// <summary>
/// Collects the live data for one info-tile of the tray popup.
/// Implementations are expected to be thread-safe and asynchronous; the
/// resolved <see cref="InfoItemData"/> is cached and refreshed periodically.
/// </summary>
public interface IInfoItemProvider
{
    /// <summary>Stable identifier matching the JSON <c>type</c> value.</summary>
    string Type { get; }

    /// <summary>Returns the most recent value (refreshing if stale).</summary>
    Task<InfoItemData> GetDataAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised whenever a periodic refresh produced a new value.</summary>
    event EventHandler<InfoItemData>? DataChanged;

    /// <summary>
    /// Apply the per-tile configuration (title overrides, thresholds, command, ...).
    /// Called once during composition and again when the config file is reloaded.
    /// </summary>
    void Configure(InfoItemConfig config);

    /// <summary>Begin the background refresh loop. Idempotent.</summary>
    void Start(TimeSpan refreshInterval);

    /// <summary>Stop the background refresh loop. Idempotent.</summary>
    void Stop();

    /// <summary>Invoke the tile's primary action (open settings, copy, etc.).</summary>
    Task ExecuteClickAsync(CancellationToken cancellationToken = default);
}
